using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DineCue.Tests;

/// <summary>
/// A monthly credit buys exactly one history entry, so anything a diner paid for has to stay reachable.
/// Before this, a finished menu scan had no list endpoint and a fit check was never stored at all.
/// </summary>
public sealed class ActivityHistoryTests
{
    [Fact]
    public async Task FitCheck_IsStoredAndCanBeReadBackLater()
    {
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        var service = TestHost.CreateRestaurantService(db, new FixedPlacesProvider(), new FixedFitAnalyzer());

        var created = await service.FitCheckAsync(
            userId,
            "place-1",
            new RestaurantFitCheckRequest("iki aile, dört çocuk", "tr", new Dictionary<string, object> { ["visitAt"] = "2026-08-08T18:00:00Z" }),
            CancellationToken.None);
        var reread = await service.GetFitCheckAsync(userId, created.Id, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.Id);
        // Exactly what the diner saw the first time.
        Assert.Equal(created.FitScore, reread.FitScore);
        Assert.Equal(created.Verdict, reread.Verdict);
        Assert.Equal(created.Summary, reread.Summary);
        Assert.Equal(created.Pros, reread.Pros);
        Assert.Equal(created.Cons, reread.Cons);
        Assert.Equal(created.QuestionsToAsk, reread.QuestionsToAsk);
        Assert.Equal(created.Confidence, reread.Confidence);
        Assert.Equal(created.ReservationAdvice, reread.ReservationAdvice);
        Assert.Equal("Harbor Meze House", reread.PlaceName);
        // Their own words are part of the record, not just our answer.
        Assert.Equal("iki aile, dört çocuk", reread.RawText);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T18:00:00Z"), (await db.RestaurantFitChecks.SingleAsync()).VisitAt);
    }

    [Fact]
    public async Task FitCheck_IsNotReadableByAnotherDiner()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateRestaurantService(db, new FixedPlacesProvider(), new FixedFitAnalyzer());
        var created = await service.FitCheckAsync(Guid.NewGuid(), "place-1", new RestaurantFitCheckRequest(null, "en", null), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetFitCheckAsync(Guid.NewGuid(), created.Id, CancellationToken.None));

        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task MenuScanList_ReturnsTheDinersOwnScansNewestFirst()
    {
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        db.MenuScans.Add(Scan(userId, "older", DateTimeOffset.UtcNow.AddHours(-2)));
        db.MenuScans.Add(Scan(userId, "newer", DateTimeOffset.UtcNow.AddMinutes(-5)));
        db.MenuScans.Add(Scan(Guid.NewGuid(), "someone else", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = TestHost.CreateMenuScan(db).Service;

        var list = await service.ListAsync(userId, 30, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal("newer", list[0].RawText);
        Assert.Equal("older", list[1].RawText);
    }

    [Fact]
    public async Task Activity_MergesEveryFlowNewestFirst()
    {
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.RecommendationSessions.Add(new RecommendationSession { UserId = userId, RawText = "dinner with kids", Status = "completed", LocationText = "Antalya", CreatedAt = now.AddMinutes(-30) });
        db.MenuScans.Add(Scan(userId, "nothing too spicy", now.AddMinutes(-20)));
        db.RestaurantFitChecks.Add(FitCheck(userId, "Harbor Meze House", 78, now.AddMinutes(-10)));
        await db.SaveChangesAsync();
        var service = new ActivityService(db);

        var feed = await service.ListAsync(userId, null, 30, CancellationToken.None);

        Assert.Equal(3, feed.Count);
        Assert.Equal([ActivityKinds.FitCheck, ActivityKinds.MenuScan, ActivityKinds.Recommendation], feed.Select(x => x.Kind));
        Assert.Equal("Harbor Meze House", feed[0].Title);
        Assert.Equal(78, feed[0].Score);
        Assert.Equal("dinner with kids", feed[2].Title);
    }

    [Theory]
    [InlineData(ActivityKinds.Recommendation)]
    [InlineData(ActivityKinds.MenuScan)]
    [InlineData(ActivityKinds.FitCheck)]
    public async Task Activity_CanBeGroupedByOneFlow(string kind)
    {
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        db.RecommendationSessions.Add(new RecommendationSession { UserId = userId, RawText = "dinner", Status = "completed" });
        db.MenuScans.Add(Scan(userId, "note", DateTimeOffset.UtcNow));
        db.RestaurantFitChecks.Add(FitCheck(userId, "A Place", 60, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new ActivityService(db);

        var grouped = await service.ListAsync(userId, kind, 30, CancellationToken.None);

        Assert.Single(grouped);
        Assert.Equal(kind, grouped[0].Kind);
    }

    [Fact]
    public async Task Activity_RejectsAnUnknownGrouping()
    {
        await using var db = TestHost.CreateDb();
        var service = new ActivityService(db);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.ListAsync(Guid.NewGuid(), "everything", 30, CancellationToken.None));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task Activity_ShowsAFailedEntryWithItsReason()
    {
        // A failed scan refunds the credit, but it still belongs in the record: the diner did something,
        // and hiding it would make the history look like it never happened.
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        var scan = Scan(userId, "blurry photo", DateTimeOffset.UtcNow);
        scan.Status = "failed";
        scan.ErrorCode = "menu_not_readable";
        db.MenuScans.Add(scan);
        await db.SaveChangesAsync();
        var service = new ActivityService(db);

        var feed = await service.ListAsync(userId, null, 30, CancellationToken.None);

        Assert.Equal("failed", feed[0].Status);
        Assert.Equal("menu_not_readable", feed[0].ErrorCode);
    }

    [Fact]
    public async Task Activity_DoesNotLeakAnotherDinersEntries()
    {
        await using var db = TestHost.CreateDb();
        db.RecommendationSessions.Add(new RecommendationSession { UserId = Guid.NewGuid(), RawText = "theirs", Status = "completed" });
        db.MenuScans.Add(Scan(Guid.NewGuid(), "theirs", DateTimeOffset.UtcNow));
        db.RestaurantFitChecks.Add(FitCheck(Guid.NewGuid(), "Theirs", 50, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new ActivityService(db);

        var feed = await service.ListAsync(Guid.NewGuid(), null, 30, CancellationToken.None);

        Assert.Empty(feed);
    }

    private static MenuScan Scan(Guid userId, string rawText, DateTimeOffset createdAt) => new()
    {
        UserId = userId,
        RawTextInput = rawText,
        Language = "tr",
        Status = "completed",
        CreatedAt = createdAt,
        RawAiResponseJson = """{"detectedLanguage":"tr","summary":"Özet.","sections":[],"items":[],"recommendations":[],"warnings":[],"questionsToAskStaff":[]}"""
    };

    private static RestaurantFitCheck FitCheck(Guid userId, string placeName, int score, DateTimeOffset createdAt) => new()
    {
        UserId = userId,
        ProviderPlaceId = "place-1",
        PlaceName = placeName,
        Address = "Addr",
        Language = "tr",
        FitScore = score,
        Verdict = "Uygun görünüyor",
        Summary = "Özet.",
        CreatedAt = createdAt
    };

    private sealed class FixedPlacesProvider : IPlacesProvider
    {
        public Task<IReadOnlyList<PlaceCandidate>> SearchAsync(string query, ResolvedLocation location, string[] cues, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaceCandidate>>([TestHost.Candidate()]);

        public Task<PlaceCandidate?> GetByPlaceIdAsync(string placeId, CancellationToken cancellationToken) =>
            Task.FromResult<PlaceCandidate?>(TestHost.Candidate());
    }

    private sealed class FixedFitAnalyzer : IAiRestaurantFitAnalyzer
    {
        public Task<RestaurantFitCheckResponse> AnalyzeAsync(RestaurantFitContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new RestaurantFitCheckResponse(
                84, "İyi uyum", "Çalışır gibi duruyor.", ["Çocuklara uygun."], ["Kalabalık olabilir."], ["Mama sandalyesi var mı?"],
                0.8, new Dictionary<string, object> { ["goodForChildren"] = "true" }, "Aramak iyi olur.", context.Reservation));
    }
}
