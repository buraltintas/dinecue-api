using System.Net;
using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class EmailProductTests
{
    private static readonly string[] Locales = ["en", "tr", "de"];
    private static readonly string[] Forbidden = ["API", "backend", "BFF", "token", "session", "provider", "OpenAI", "Google Places", "Resend"];

    [Fact]
    public void OtpWelcomeAndMonthlyRecapTemplates_RenderLocalizedSafeHtmlAndText()
    {
        var renderer = new EmailTemplateRenderer();
        foreach (var locale in Locales)
        {
            var otp = renderer.RenderEmailVerification(new EmailTemplateModel(locale, Code: "123456", ExpiresInMinutes: 5));
            var welcome = renderer.RenderWelcome(new EmailTemplateModel(locale, DisplayName: "Ada", LinkUrl: "https://app.example.com/app/find"));
            var recap = renderer.RenderMonthlyRecap(new MonthlyRecapEmailModel(locale, "2026-06", 2, [new("Sade Sofra", "Warm and simple", "It fit the evening well.")], "https://app.example.com/app/history"));

            AssertTemplate(otp);
            AssertTemplate(welcome);
            AssertTemplate(recap);
            Assert.Contains("DineCue", otp.HtmlBody);
            Assert.Contains("DineCue", welcome.HtmlBody);
            Assert.Contains("DineCue", recap.HtmlBody);
            Assert.DoesNotContain("text-transform", otp.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("text-transform", welcome.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("text-transform", recap.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<a ", otp.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", otp.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", otp.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("123456", otp.TextBody);
        }
    }

    [Fact]
    public void TemplateLocale_FallsBackToEnglish()
    {
        var template = new EmailTemplateRenderer().RenderEmailVerification(new EmailTemplateModel("fr", Code: "123456", ExpiresInMinutes: 5));

        Assert.Equal("Your sign-in code", template.Subject);
    }

    [Fact]
    public async Task ResendSender_UsesConfigurableReplyTo()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"id":"email-1"}""");
        var sender = new ResendEmailSender(
            new HttpClient(handler),
            Options.Create(new EmailOptions
            {
                Provider = "resend",
                FromEmail = "hello@dinecue.com",
                FromName = "DineCue",
                ReplyToEmail = "support@dinecue.com",
                ResendApiKey = "test-key",
                TimeoutSeconds = 10
            }),
            Options.Create(new EmailOtpOptions { ExpiryMinutes = 5 }),
            new EmailTemplateRenderer(),
            NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync(new EmailMessage("person@example.com", "Hello", "<p>Hello</p>", "Hello", "en"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("support@dinecue.com", handler.Request?.Headers.GetValues("Reply-To").Single());
        Assert.Contains("hello@dinecue.com", handler.Body);
    }

    [Fact]
    public async Task MonthlyRecap_DoesNotSendWithoutActivity()
    {
        await using var db = CreateDb();
        db.Users.Add(new User { Email = "person@example.com", PreferredLanguage = "en" });
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();
        var service = CreateRecapService(db, sender, enabledByDefault: true);

        var sent = await service.SendMonthlyRecapsAsync("2026-06", CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task MonthlyRecap_UsesStoredRecommendationDataAndIsIdempotent()
    {
        await using var db = CreateDb();
        var user = SeedCompletedRecommendation(db, "en", new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();
        var service = CreateRecapService(db, sender, enabledByDefault: true);

        var first = await service.SendMonthlyRecapsAsync("2026-06", CancellationToken.None);
        var second = await service.SendMonthlyRecapsAsync("2026-06", CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var message = Assert.Single(sender.Messages);
        Assert.Contains("Sade Sofra", message.TextBody);
        Assert.Contains("A warm fit from stored copy.", message.TextBody);
        Assert.DoesNotContain("Invented", message.TextBody);
        Assert.Equal("monthly_recap", message.Metadata?["template"]);
        Assert.Equal(1, await db.EmailDeliveryLedgers.CountAsync(x => x.UserId == user.Id && x.EmailType == "monthly_recap" && x.Status == "sent"));
    }

    [Fact]
    public async Task MonthlyRecap_RespectsOptOut()
    {
        await using var db = CreateDb();
        var user = SeedCompletedRecommendation(db, "en", new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, MonthlyRecapEnabled = false });
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        var sent = await CreateRecapService(db, sender, enabledByDefault: true).SendMonthlyRecapsAsync("2026-06", CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task MonthlyRecap_ProviderFailureDoesNotThrowAndCanRetry()
    {
        await using var db = CreateDb();
        var user = SeedCompletedRecommendation(db, "en", new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        var failing = new CapturingEmailSender(succeed: false);
        var failedCount = await CreateRecapService(db, failing, enabledByDefault: true).SendMonthlyRecapsAsync("2026-06", CancellationToken.None);
        var retry = new CapturingEmailSender();
        var retryCount = await CreateRecapService(db, retry, enabledByDefault: true).SendMonthlyRecapsAsync("2026-06", CancellationToken.None);

        Assert.Equal(0, failedCount);
        Assert.Equal(1, retryCount);
        Assert.Equal("sent", (await db.EmailDeliveryLedgers.SingleAsync(x => x.UserId == user.Id && x.EmailType == "monthly_recap")).Status);
    }

    private static void AssertTemplate(RenderedEmailTemplate template)
    {
        Assert.False(string.IsNullOrWhiteSpace(template.Subject));
        Assert.False(string.IsNullOrWhiteSpace(template.HtmlBody));
        Assert.False(string.IsNullOrWhiteSpace(template.TextBody));
        foreach (var word in Forbidden)
        {
            Assert.DoesNotContain(word, template.Subject, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(word, template.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(word, template.TextBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static MonthlyRecapEmailService CreateRecapService(DineCueDbContext db, IEmailSender sender, bool enabledByDefault) =>
        new(
            db,
            sender,
            new EmailTemplateRenderer(),
            Options.Create(new EmailOptions { AppBaseUrl = "https://app.example.com" }),
            Options.Create(new ProductEmailOptions { MonthlyRecapEnabled = true, MonthlyRecapEnabledByDefault = enabledByDefault, HistoryUrlPath = "/app/history" }),
            NullLogger<MonthlyRecapEmailService>.Instance);

    private static User SeedCompletedRecommendation(DineCueDbContext db, string language, DateTimeOffset completedAt)
    {
        var user = new User { Email = Guid.NewGuid().ToString("N") + "@example.com", PreferredLanguage = language };
        var session = new RecommendationSession { UserId = user.Id, Status = "completed", CompletedAt = completedAt, CreatedAt = completedAt, RawText = "Dinner" };
        var candidate = new RecommendationCandidate { SessionId = session.Id, ProviderPlaceId = "place-1", Name = "Sade Sofra", Address = "Address" };
        var result = new RecommendationResult
        {
            SessionId = session.Id,
            CandidateId = candidate.Id,
            Rank = 1,
            Headline = "Stored headline",
            WhyThisPlace = "A warm fit from stored copy.",
            Summary = "Stored summary"
        };
        db.Users.Add(user);
        db.RecommendationSessions.Add(session);
        db.RecommendationCandidates.Add(candidate);
        db.RecommendationResults.Add(result);
        return user;
    }

    private static DineCueDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DineCueDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class CapturingEmailSender(bool succeed = true) : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(succeed ? new EmailSendResult(true, "email-1") : new EmailSendResult(false, ErrorCode: "provider_failed"));
        }

        public Task<EmailSendResult> SendOtpAsync(string email, string code, string? locale, CancellationToken cancellationToken) =>
            SendAsync(new EmailMessage(email, "OTP", "", "", locale ?? "en", new Dictionary<string, string> { ["template"] = "email_verification" }), cancellationToken);
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        }
    }
}
