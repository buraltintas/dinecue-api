using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DineCue.Tests;

public sealed class FitCheckTests
{
    [Fact]
    public async Task FitCheck_SpendsOneCreditAndPassesTheStoredProfileAndIntendedTime()
    {
        await using var db = TestHost.CreateDb();
        var userId = Guid.NewGuid();
        db.TasteProfiles.Add(new TasteProfile { UserId = userId, AllergiesJson = """["shellfish"]""" });
        db.DiningProfiles.Add(new DiningProfile { UserId = userId, UsuallyWithKids = true, BudgetSensitivity = 8 });
        await db.SaveChangesAsync();
        var quota = TestHost.CreateQuotaService(db);
        var analyzer = new RecordingFitAnalyzer();
        var service = TestHost.CreateRestaurantService(db, new StubPlacesProvider(), analyzer, quota);

        var request = new RestaurantFitCheckRequest(
            "iki aile, 4 çocuk, cumartesi akşamı",
            "tr",
            new Dictionary<string, object> { ["partySize"] = 8, ["visitAt"] = "2026-08-08T21:00:00+03:00" });
        var response = await service.FitCheckAsync(userId, "place-1", request, CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal(84, response.FitScore);
        Assert.Equal(1, state.UsedThisPeriod);
        Assert.NotNull(analyzer.LastContext);
        Assert.Equal(["shellfish"], analyzer.LastContext!.TasteProfile?.Allergies);
        Assert.True(analyzer.LastContext.DiningProfile?.UsuallyWithKids);
        Assert.Equal(8, analyzer.LastContext.DiningProfile?.BudgetSensitivity);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T21:00:00+03:00"), analyzer.LastContext.VisitAt);
        Assert.Equal("tr", analyzer.LastContext.Request.Language);
    }

    [Fact]
    public async Task FitCheck_ReturnsTheCreditWhenTheProviderFails()
    {
        await using var db = TestHost.CreateDb();
        var quota = TestHost.CreateQuotaService(db);
        var service = TestHost.CreateRestaurantService(db, new StubPlacesProvider(), new ThrowingFitAnalyzer(), quota);
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<ProviderFailureException>(() =>
            service.FitCheckAsync(userId, "place-1", new RestaurantFitCheckRequest(null, "en", null), CancellationToken.None));
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal(0, state.UsedThisPeriod);
    }

    [Fact]
    public async Task FitCheck_ReturnsTheCreditWhenThePlaceDoesNotExist()
    {
        await using var db = TestHost.CreateDb();
        var quota = TestHost.CreateQuotaService(db);
        var service = TestHost.CreateRestaurantService(db, new StubPlacesProvider { Found = false }, new RecordingFitAnalyzer(), quota);
        var userId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.FitCheckAsync(userId, "place-missing", new RestaurantFitCheckRequest(null, "en", null), CancellationToken.None));
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("not_found", error.Code);
        Assert.Equal(0, state.UsedThisPeriod);
    }

    [Fact]
    public async Task RestaurantDetails_ReturnProviderValuesRatherThanPlaceholders()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateRestaurantService(db, new StubPlacesProvider(), new RecordingFitAnalyzer());

        var details = await service.GetAsync("place-1", CancellationToken.None);

        // These three used to be hard-coded example.com and +90 242 000 0000 values.
        Assert.Equal("https://harbormeze.example", details.WebsiteUrl);
        Assert.Equal("https://maps.example/harbor", details.GoogleMapsUri);
        Assert.Equal("+90 242 111 2233", details.PhoneNumber);
    }

    [Fact]
    public void ReadVisitAt_ParsesAnIsoTimestampAndIgnoresAnythingElse()
    {
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-08T21:00:00+03:00"),
            RestaurantService.ReadVisitAt(new Dictionary<string, object> { ["visitAt"] = "2026-08-08T21:00:00+03:00" }));
        Assert.Null(RestaurantService.ReadVisitAt(null));
        Assert.Null(RestaurantService.ReadVisitAt(new Dictionary<string, object>()));
        Assert.Null(RestaurantService.ReadVisitAt(new Dictionary<string, object> { ["visitAt"] = "saturday evening" }));
    }

    // --- Analyzer parsing -----------------------------------------------------------------------------

    [Fact]
    public async Task Analyzer_KeepsFitScoreAndConfidenceInTheContractedRanges()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.OpenAi("""
        {
          "fitScore": 78,
          "verdict": "Uygun görünüyor",
          "summary": "Cumartesi akşamı için çalışıyor gibi duruyor.",
          "pros": ["Çocuklara uygun olduğu belirtilmiş."],
          "cons": ["Yoğun saatlerde kalabalık olabilir."],
          "questionsToAsk": ["Mama sandalyeniz var mı?"],
          "confidence": 0.72,
          "reservationAdvice": "Gitmeden önce aramak iyi olur.",
          "relevantSignals": [{ "signal": "goodForChildren", "value": "true", "impact": "positive" }]
        }
        """));
        var analyzer = new OpenAiRestaurantFitAnalyzer(TestHost.CreateOpenAiClient(handler), NullLogger<OpenAiRestaurantFitAnalyzer>.Instance);

        var response = await analyzer.AnalyzeAsync(Context(), CancellationToken.None);

        Assert.Equal(78, response.FitScore);
        // The UI renders confidence as a 0-1 fraction; this must never become a percentage.
        Assert.Equal(0.72, response.Confidence);
        Assert.Equal("Uygun görünüyor", response.Verdict);
        Assert.Single(response.Pros);
        Assert.True(response.RelevantSignals.ContainsKey("goodForChildren"));
        Assert.Equal("website_or_phone", response.Reservation.Status);
    }

    [Fact]
    public async Task Analyzer_AsksForARepairWhenTheScoreIsOutOfRange()
    {
        var handler = new TestHost.StubHttpHandler(
            TestHost.StubHttpHandler.OpenAi("""{"fitScore":180,"verdict":"Great","summary":"s","pros":[],"cons":[],"questionsToAsk":[],"confidence":0.5,"reservationAdvice":"a","relevantSignals":[]}"""),
            TestHost.StubHttpHandler.OpenAi("""{"fitScore":72,"verdict":"Great","summary":"s","pros":[],"cons":[],"questionsToAsk":[],"confidence":0.5,"reservationAdvice":"a","relevantSignals":[]}"""));
        var analyzer = new OpenAiRestaurantFitAnalyzer(TestHost.CreateOpenAiClient(handler), NullLogger<OpenAiRestaurantFitAnalyzer>.Instance);

        var response = await analyzer.AnalyzeAsync(Context(), CancellationToken.None);

        Assert.Equal(72, response.FitScore);
        Assert.Equal(2, handler.RequestCount);
        // The second attempt must actually carry the correction, not just retry blindly.
        Assert.Contains("fitScore", handler.RequestBodies[1]);
        Assert.Contains("repairFeedback", handler.RequestBodies[1]);
    }

    [Fact]
    public void AnalyzerPrompt_GroundsTheAnswerAndCarriesTheIntendedTime()
    {
        var prompt = OpenAiRestaurantFitAnalyzer.BuildPrompt(Context(), repairFeedback: null);

        Assert.Contains("Do not claim the place is quiet", prompt);
        Assert.Contains("openingPeriods", prompt);
        Assert.Contains("2026-08-08T21:00:00", prompt);
        Assert.Contains("goodForChildren", prompt);
        Assert.Contains("shellfish", prompt);
        // It has not seen a menu, so it must not describe dishes.
        Assert.Contains("you have not seen the menu", prompt);
    }

    private static RestaurantFitContext Context() => new(
        TestHost.Candidate(metadataJson: """{"reservable":true,"websiteUri":"https://harbormeze.example","phoneNumber":"+90 242 111 2233","goodForChildren":true,"openingHours":["Saturday: 12:00 - 23:00"]}"""),
        new RestaurantFitCheckRequest("iki aile, 4 çocuk", "tr", new Dictionary<string, object> { ["partySize"] = 8 }),
        new ReservationDto("website_or_phone", "restaurant_website", "https://harbormeze.example", "+90 242 111 2233", 0.8),
        TestHost.Taste(allergies: ["shellfish"]),
        new DiningProfileDto(true, false, true, 8, true, false, false, false, 1800),
        DateTimeOffset.Parse("2026-08-08T21:00:00+03:00"));

    private sealed class StubPlacesProvider : IPlacesProvider
    {
        public bool Found { get; init; } = true;

        public Task<IReadOnlyList<PlaceCandidate>> SearchAsync(string query, ResolvedLocation location, string[] cues, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaceCandidate>>([Candidate()]);

        public Task<PlaceCandidate?> GetByPlaceIdAsync(string placeId, CancellationToken cancellationToken) =>
            Task.FromResult(Found ? Candidate() : null);

        private static PlaceCandidate Candidate() => TestHost.Candidate(
            metadataJson: """{"websiteUri":"https://harbormeze.example","googleMapsUri":"https://maps.example/harbor","phoneNumber":"+90 242 111 2233","reservable":true}""");
    }

    private sealed class RecordingFitAnalyzer : IAiRestaurantFitAnalyzer
    {
        public RestaurantFitContext? LastContext { get; private set; }

        public Task<RestaurantFitCheckResponse> AnalyzeAsync(RestaurantFitContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(new RestaurantFitCheckResponse(
                84, "Good fit", "Looks workable.", ["Kid friendly."], ["Can get busy."], ["High chairs?"], 0.8,
                new Dictionary<string, object>(), "Call ahead.", context.Reservation));
        }
    }

    private sealed class ThrowingFitAnalyzer : IAiRestaurantFitAnalyzer
    {
        public Task<RestaurantFitCheckResponse> AnalyzeAsync(RestaurantFitContext context, CancellationToken cancellationToken) =>
            throw new ProviderFailureException("fit_provider_failed", "We could not check this place right now.");
    }
}
