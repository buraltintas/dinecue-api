using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class QuotaServiceTests
{
    [Fact]
    public async Task NewUser_DefaultsToFreeMonthlyQuota()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var state = await service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("free", state.Plan);
        Assert.Equal(5, state.MonthlyLimit);
        Assert.Equal(0, state.UsedThisPeriod);
        Assert.Equal(5, state.RemainingThisPeriod);
        Assert.Equal(CurrentPeriodKey(), state.PeriodKey);
        Assert.True(state.ProAvailable);
        Assert.Equal("coming_soon", state.ProStatus);
    }

    [Fact]
    public async Task FreePlan_AllowsFiveMonthlyCredits()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var userId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await service.ReserveRecommendationAsync(userId, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReserveMenuScanAsync(userId, CancellationToken.None));
        var state = await service.GetAsync(userId, CancellationToken.None);

        Assert.Equal("quota_exceeded", error.Code);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal(5, state.UsedThisPeriod);
        Assert.Equal(0, state.RemainingThisPeriod);
    }

    [Fact]
    public async Task ProPlan_AllowsFiftyMonthlyCredits()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Subscriptions.Add(new Subscription { UserId = userId, PlanType = "pro", IsActive = true });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        for (var i = 0; i < 50; i++)
            await service.ReserveRestaurantFitCheckAsync(userId, CancellationToken.None);

        var state = await service.GetAsync(userId, CancellationToken.None);
        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReserveRecommendationAsync(userId, CancellationToken.None));

        Assert.Equal("pro", state.Plan);
        Assert.Equal(50, state.MonthlyLimit);
        Assert.Equal(50, state.UsedThisPeriod);
        Assert.Equal("quota_exceeded", error.Code);
    }

    [Fact]
    public async Task UsageResetsForNewCalendarMonth()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var previousMonth = CurrentPeriodStart().AddMonths(-1);
        db.DailyUsages.Add(new DailyUsage
        {
            UserId = userId,
            UsageDate = previousMonth,
            RecommendationSessionCount = 5
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var state = await service.GetAsync(userId, CancellationToken.None);

        Assert.Equal(0, state.UsedThisPeriod);
        Assert.Equal(5, state.RemainingThisPeriod);
    }

    [Fact]
    public async Task QuotaExceededError_UsesStableClientShape()
    {
        await using var db = CreateDb();
        var service = CreateService(db, monthlyFree: 1, monthlyPro: 2);
        var userId = Guid.NewGuid();

        await service.ReserveRecommendationAsync(userId, CancellationToken.None);
        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReserveRecommendationAsync(userId, CancellationToken.None));

        Assert.Equal("quota_exceeded", error.Code);
        Assert.Equal("You have reached your monthly plan limit.", error.Message);
        Assert.NotNull(error.Details);
        Assert.Contains("monthlyLimit", error.Details.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("periodKey", error.Details.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecommendationProviderFailure_ReleasesReservedCredit()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var userId = Guid.NewGuid();

        await service.ReserveRecommendationAsync(userId, CancellationToken.None);
        await service.ReleaseRecommendationAsync(userId, CancellationToken.None);
        var state = await service.GetAsync(userId, CancellationToken.None);

        Assert.Equal(0, state.UsedThisPeriod);
        Assert.Equal(5, state.RemainingThisPeriod);
    }

    [Fact]
    public async Task RecommendationQuotaExceeded_DoesNotCreateSessionOrEnqueueProviderJob()
    {
        await using var db = CreateDb();
        var quota = CreateService(db, monthlyFree: 1, monthlyPro: 50);
        var userId = Guid.NewGuid();
        await quota.ReserveRecommendationAsync(userId, CancellationToken.None);
        var queue = new RecordingRecommendationJobQueue();
        var service = new RecommendationService(db, quota, queue, new NoopInteractionEventService());

        var error = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            userId,
            new RecommendationSessionRequest("Dinner nearby", new LocationInput("text", "Antalya", null, null, null), [], "en", null),
            CancellationToken.None));

        Assert.Equal("quota_exceeded", error.Code);
        Assert.Equal(0, queue.EnqueueCount);
        Assert.Empty(db.RecommendationSessions);
    }

    [Fact]
    public void ProfileDto_DoesNotExposePlanOrQuotaFields()
    {
        var names = typeof(ProfileDto).GetProperties().Select(x => x.Name).ToArray();

        Assert.DoesNotContain(names, x => x.Contains("Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Quota", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SameGoogleProviderIdentity_CannotResetFreeQuotaByRecreatingAccount()
    {
        await using var db = CreateDb();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        db.Users.Add(new User { Id = firstUser, Email = "first@example.com" });
        db.UserIdentities.Add(new UserIdentity { UserId = firstUser, Provider = "google", ProviderUserId = "google-sub-1", Email = "first@example.com" });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        for (var i = 0; i < 5; i++)
            await service.ReserveRecommendationAsync(firstUser, CancellationToken.None);

        db.Users.Add(new User { Id = secondUser, Email = "second@example.com" });
        db.UserIdentities.Add(new UserIdentity { UserId = secondUser, Provider = "google", ProviderUserId = "google-sub-1", Email = "second@example.com" });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReserveRecommendationAsync(secondUser, CancellationToken.None));

        Assert.Equal("quota_exceeded", error.Code);
    }

    [Fact]
    public async Task SameNormalizedEmail_CannotResetFreeQuotaByRecreatingAccount()
    {
        await using var db = CreateDb();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        db.Users.Add(new User { Id = firstUser, Email = "person@example.com" });
        db.UserIdentities.Add(new UserIdentity { UserId = firstUser, Provider = "email", ProviderUserId = "person@example.com", Email = "person@example.com" });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        for (var i = 0; i < 5; i++)
            await service.ReserveMenuScanAsync(firstUser, CancellationToken.None);

        db.ChangeTracker.Clear();
        db.Users.Add(new User { Id = secondUser, Email = "person@example.com" });
        db.UserIdentities.Add(new UserIdentity { UserId = secondUser, Provider = "email", ProviderUserId = "person@example.com", Email = "person@example.com" });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReserveRestaurantFitCheckAsync(secondUser, CancellationToken.None));

        Assert.Equal("quota_exceeded", error.Code);
    }

    [Fact]
    public void OtpRequests_AreRateLimitedWithStableSafeCode()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new OtpRateLimiter(
            cache,
            Options.Create(new EmailOtpOptions { StartLimitPerEmailWindow = 1, EmailWindowMinutes = 15 }),
            CreateAbuseProtection(cache));

        limiter.CheckStart("person@example.com");
        var error = Assert.Throws<ApiException>(() => limiter.CheckStart("person@example.com"));

        Assert.Equal("too_many_attempts", error.Code);
        Assert.DoesNotContain("abuse", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisposableEmailDenylist_BlocksThroughExtensionPoint()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var protection = CreateAbuseProtection(cache, disposableDomains: ["mailinator.test"]);

        var error = Assert.Throws<ApiException>(() => protection.EnsureEmailAllowed("person@mailinator.test"));

        Assert.Equal("temporarily_limited", error.Code);
        Assert.DoesNotContain("disposable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountCreation_IsThrottledByStableSignals()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var protection = CreateAbuseProtection(cache, accountLimit: 1);

        protection.CheckAccountCreation("google", "provider-subject", "person@example.com");
        var error = Assert.Throws<ApiException>(() => protection.CheckAccountCreation("google", "provider-subject", "person@example.com"));

        Assert.Equal("too_many_attempts", error.Code);
    }

    private static QuotaService CreateService(DineCueDbContext db, int monthlyFree = 5, int monthlyPro = 50) =>
        new(db, new MockSubscriptionProvider(db), Options.Create(new QuotasOptions
        {
            MonthlyFree = monthlyFree,
            MonthlyPro = monthlyPro
        }), Options.Create(new JwtOptions { SigningKey = SigningKey }));

    private static AbuseProtectionService CreateAbuseProtection(IMemoryCache cache, string[]? disposableDomains = null, int accountLimit = 3) =>
        new(
            cache,
            Options.Create(new AbuseProtectionOptions
            {
                DisposableEmailDomains = disposableDomains ?? [],
                AccountCreationLimitPerEmailWindow = accountLimit,
                AccountCreationLimitPerProviderWindow = accountLimit,
                AccountCreationWindowMinutes = 60
            }),
            Options.Create(new JwtOptions { SigningKey = SigningKey }));

    private const string SigningKey = "test-signing-key-at-least-32-characters-long";

    private static DineCueDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DineCueDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DateOnly CurrentPeriodStart()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateOnly(now.Year, now.Month, 1);
    }

    private static string CurrentPeriodKey()
    {
        var period = CurrentPeriodStart();
        return $"{period.Year:D4}-{period.Month:D2}";
    }

    private sealed class RecordingRecommendationJobQueue : IRecommendationJobQueue
    {
        public int EnqueueCount { get; private set; }

        public ValueTask EnqueueAsync(RecommendationJob job, CancellationToken cancellationToken)
        {
            EnqueueCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RecommendationJob> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopInteractionEventService : IInteractionEventService
    {
        public Task TrackAsync(Guid userId, InteractionEventRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
