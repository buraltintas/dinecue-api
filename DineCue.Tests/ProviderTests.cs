using System.Net;
using System.Text.Json.Nodes;
using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class ProviderTests
{
    // --- Places normalization -------------------------------------------------------------------------

    [Fact]
    public void NormalizePlace_CarriesTheGroundingMetadataThePromptRelisOn()
    {
        var place = JsonNode.Parse("""
        {
          "id": "place-1",
          "displayName": { "text": "Harbor Meze House" },
          "formattedAddress": "Iskele Cad. No:4, Antalya",
          "location": { "latitude": 36.8862, "longitude": 30.7048 },
          "rating": 4.5,
          "userRatingCount": 531,
          "priceLevel": "PRICE_LEVEL_MODERATE",
          "types": ["restaurant", "point_of_interest"],
          "primaryType": "restaurant",
          "googleMapsUri": "https://maps.google.com/?cid=1",
          "websiteUri": "https://harbormeze.example",
          "nationalPhoneNumber": "+90 242 111 2233",
          "editorialSummary": { "text": "Seafood meze by the old harbour." },
          "currentOpeningHours": { "openNow": true },
          "regularOpeningHours": {
            "weekdayDescriptions": ["Monday: 12:00 - 23:00"],
            "periods": [{ "open": { "day": 1, "hour": 12, "minute": 0 }, "close": { "day": 1, "hour": 23, "minute": 0 } }]
          },
          "reservable": true,
          "goodForChildren": true,
          "outdoorSeating": true,
          "servesVegetarianFood": false,
          "parkingOptions": { "freeParkingLot": true, "valetParking": false },
          "photos": [{ "name": "photo-1" }, { "name": "photo-2" }]
        }
        """);

        var candidate = GooglePlacesApi.NormalizePlace(place);

        Assert.NotNull(candidate);
        Assert.Equal("google_places", candidate!.Provider);
        Assert.Equal(2, candidate.PriceLevel);
        Assert.Equal(531, candidate.RatingCount);
        var metadata = JsonNode.Parse(candidate.RawPayloadJson)!;
        Assert.Equal("+90 242 111 2233", metadata["phoneNumber"]!.GetValue<string>());
        Assert.Equal("Seafood meze by the old harbour.", metadata["editorialSummary"]!.GetValue<string>());
        Assert.True(metadata["openNow"]!.GetValue<bool>());
        Assert.True(metadata["reservable"]!.GetValue<bool>());
        Assert.True(metadata["goodForChildren"]!.GetValue<bool>());
        Assert.True(metadata["outdoorSeating"]!.GetValue<bool>());
        Assert.False(metadata["servesVegetarianFood"]!.GetValue<bool>());
        Assert.Single(metadata["openingHours"]!.AsArray());
        Assert.Single(metadata["openingPeriods"]!.AsArray());
        Assert.Equal(2, metadata["photoCount"]!.GetValue<int>());
        // Only the true flags survive, so the model cannot read absence as a negative claim.
        Assert.True(metadata["parkingOptions"]!["freeParkingLot"]!.GetValue<bool>());
        Assert.Null(metadata["parkingOptions"]!["valetParking"]);
    }

    [Fact]
    public void NormalizePlace_DropsPlacesWeMustNotRecommend()
    {
        var closed = JsonNode.Parse("""{"id":"a","businessStatus":"CLOSED_PERMANENTLY","types":["restaurant"],"location":{"latitude":1,"longitude":2}}""");
        var notFood = JsonNode.Parse("""{"id":"b","types":["pharmacy"],"primaryType":"pharmacy","location":{"latitude":1,"longitude":2}}""");
        var noCoordinates = JsonNode.Parse("""{"id":"c","types":["restaurant"]}""");

        Assert.Null(GooglePlacesApi.NormalizePlace(closed));
        Assert.Null(GooglePlacesApi.NormalizePlace(notFood));
        Assert.Null(GooglePlacesApi.NormalizePlace(noCoordinates));
    }

    [Fact]
    public void ResolveRadiusMeters_PrefersTheDinersOwnDistancePreference()
    {
        var config = new GooglePlacesOptions { DefaultRadiusMeters = 2500 };

        Assert.Equal(2500, GooglePlacesApi.ResolveRadiusMeters(null, config));
        Assert.Equal(1800, GooglePlacesApi.ResolveRadiusMeters(DiningProfile(1800), config));
        // Clamped to a sane band so a stored extreme cannot produce a useless search.
        Assert.Equal(500, GooglePlacesApi.ResolveRadiusMeters(DiningProfile(50), config));
        Assert.Equal(20000, GooglePlacesApi.ResolveRadiusMeters(DiningProfile(999999), config));
    }

    // --- Location resolution --------------------------------------------------------------------------

    [Fact]
    public async Task LocationResolver_ResolvesTypedTextToRealCoordinates()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Json("""
        {"places":[{"id":"berlin-mitte","location":{"latitude":52.5200,"longitude":13.4050},"formattedAddress":"Mitte, Berlin"}]}
        """));
        var resolver = CreateLocationResolver(handler);

        var resolved = await resolver.ResolveAsync(new LocationInput("text", "Berlin Mitte", null, null, null), CancellationToken.None);

        Assert.Equal(52.52, resolved.Latitude);
        Assert.Equal(13.405, resolved.Longitude);
        Assert.Equal("Mitte, Berlin", resolved.Text);
        Assert.Equal("text_resolved", resolved.Assumptions["locationConfidence"]);
    }

    [Fact]
    public async Task LocationResolver_WhenLookupFails_ReturnsNoCoordinatesInsteadOfInventingThem()
    {
        // The mock resolver this replaced returned fixed Antalya coordinates here, which silently searched
        // the wrong city for every text-mode request.
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Status(HttpStatusCode.InternalServerError));
        var resolver = CreateLocationResolver(handler);

        var resolved = await resolver.ResolveAsync(new LocationInput("text", "Berlin Mitte", null, null, null), CancellationToken.None);

        Assert.Null(resolved.Latitude);
        Assert.Null(resolved.Longitude);
        Assert.Equal("Berlin Mitte", resolved.Text);
        Assert.Equal("unresolved", resolved.Assumptions["locationConfidence"]);
    }

    [Fact]
    public async Task LocationResolver_KeepsSuppliedCoordinatesWithoutCallingTheProvider()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Status(HttpStatusCode.InternalServerError));
        var resolver = CreateLocationResolver(handler);

        var resolved = await resolver.ResolveAsync(new LocationInput("current", null, 40.9909, 29.0303, null), CancellationToken.None);

        Assert.Equal(40.9909, resolved.Latitude);
        Assert.Equal("coordinates", resolved.Assumptions["locationConfidence"]);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task LocationResolver_CachesAResolvedPlaceName()
    {
        var handler = new TestHost.StubHttpHandler(
            TestHost.StubHttpHandler.Json("""{"places":[{"id":"p","location":{"latitude":1,"longitude":2},"formattedAddress":"Somewhere"}]}"""));
        var resolver = CreateLocationResolver(handler);

        await resolver.ResolveAsync(new LocationInput("text", "Kadıköy", null, null, null), CancellationToken.None);
        await resolver.ResolveAsync(new LocationInput("text", "kadıköy", null, null, null), CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    // --- Candidate search -----------------------------------------------------------------------------

    [Fact]
    public async Task PlacesSearch_RequestsTheRichFieldMaskAndBiasesToThePreferredRadius()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Json(SearchPayload("place-1")));
        var provider = CreateSearchProvider(handler);
        var intent = TestHost.Intent(
            location: new LocationInput("current", "Antalya", 36.88, 30.70, null),
            diningProfile: DiningProfile(1800));

        var candidates = await provider.SearchAsync(intent, CancellationToken.None);

        Assert.NotEmpty(candidates);
        Assert.Contains("places.goodForChildren", handler.FieldMasks[0]);
        Assert.Contains("places.regularOpeningHours.periods", handler.FieldMasks[0]);
        Assert.Contains("\"radius\":1800", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task PlacesSearch_ServesTheSecondIdenticalSearchFromCache()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Json(SearchPayload("place-1")));
        var provider = CreateSearchProvider(handler);
        var intent = TestHost.Intent(location: new LocationInput("current", "Antalya", 36.88, 30.70, null));

        await provider.SearchAsync(intent, CancellationToken.None);
        var requestsAfterFirst = handler.RequestCount;
        await provider.SearchAsync(intent, CancellationToken.None);

        // Refining a session must not pay the provider twice for the same query set.
        Assert.Equal(requestsAfterFirst, handler.RequestCount);
    }

    [Fact]
    public async Task PlacesSearch_OmitsLocationBiasWhenTheLocationCouldNotBeResolved()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Json(SearchPayload("place-1")));
        var provider = CreateSearchProvider(handler);
        var intent = TestHost.Intent(location: new LocationInput("text", "Berlin Mitte", null, null, null));

        await provider.SearchAsync(intent, CancellationToken.None);

        Assert.DoesNotContain("locationBias", handler.RequestBodies[0]);
        Assert.Contains("Berlin Mitte", handler.RequestBodies[0]);
    }

    // --- Snapshot cache -------------------------------------------------------------------------------

    [Fact]
    public async Task SnapshotStore_UpsertIsIdempotentAndRefreshesTheStoredFields()
    {
        using var provider = BuildScopedProvider(out var databaseName);
        var store = new RestaurantSnapshotStore(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RestaurantSnapshotStore>.Instance);
        var first = TestHost.Candidate(metadataJson: """{"phoneNumber":"+90 111","websiteUri":"https://a.example","openingHours":["Monday: 12:00 - 23:00"]}""");
        var second = first with { Name = "Harbor Meze House (renovated)", Rating = 4.7m };

        await store.UpsertAsync([first], CancellationToken.None);
        await store.UpsertAsync([second], CancellationToken.None);

        await using var db = OpenDb(databaseName);
        var snapshot = await db.RestaurantSnapshots.SingleAsync();
        Assert.Equal("Harbor Meze House (renovated)", snapshot.Name);
        Assert.Equal(4.7m, snapshot.Rating);
        Assert.Equal("+90 111", snapshot.PhoneNumber);
        Assert.Equal("https://a.example", snapshot.WebsiteUrl);
    }

    [Fact]
    public async Task SnapshotStore_IgnoresEntriesOlderThanTheRequestedFreshness()
    {
        using var provider = BuildScopedProvider(out var databaseName);
        var store = new RestaurantSnapshotStore(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RestaurantSnapshotStore>.Instance);
        await store.UpsertAsync([TestHost.Candidate()], CancellationToken.None);

        var fresh = await store.TryGetFreshAsync("place-1", TimeSpan.FromHours(1), CancellationToken.None);
        var stale = await store.TryGetFreshAsync("place-1", TimeSpan.Zero, CancellationToken.None);
        var missing = await store.TryGetFreshAsync("place-unknown", TimeSpan.FromHours(1), CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.Equal("Harbor Meze House", fresh!.Name);
        Assert.Null(stale);
        Assert.Null(missing);
    }

    [Fact]
    public async Task RestaurantDetails_ReadFromCacheDoNotCallTheProviderAgain()
    {
        using var provider = BuildScopedProvider(out _);
        var store = new RestaurantSnapshotStore(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RestaurantSnapshotStore>.Instance);
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Json(DetailsPayload("place-1")));
        var places = new GooglePlacesRestaurantProvider(
            new HttpClient(handler),
            Options.Create(new GooglePlacesOptions { ApiKey = "key" }),
            store,
            NullLogger<GooglePlacesRestaurantProvider>.Instance);

        var first = await places.GetByPlaceIdAsync("place-1", CancellationToken.None);
        var second = await places.GetByPlaceIdAsync("place-1", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, handler.RequestCount);
    }

    // --- Reservation ----------------------------------------------------------------------------------

    [Fact]
    public async Task ReservationResolver_DerivesTheAffordanceFromProviderMetadataOnly()
    {
        var resolver = new PlacesReservationLinkResolver();

        var reservable = await resolver.ResolveAsync(
            TestHost.Candidate(metadataJson: """{"reservable":true,"websiteUri":"https://book.example","phoneNumber":"+90 242 111 2233"}"""),
            CancellationToken.None);
        var phoneOnly = await resolver.ResolveAsync(
            TestHost.Candidate(metadataJson: """{"phoneNumber":"+90 242 111 2233","googleMapsUri":"https://maps.example"}"""),
            CancellationToken.None);
        var nothingKnown = await resolver.ResolveAsync(TestHost.Candidate(), CancellationToken.None);

        Assert.Equal("website_or_phone", reservable.Status);
        Assert.Equal("https://book.example", reservable.Url);
        Assert.Equal("+90 242 111 2233", reservable.PhoneNumber);

        Assert.Equal("phone_only", phoneOnly.Status);
        Assert.Equal("https://maps.example", phoneOnly.Url);

        // With no signal we still hand back a maps link, but claim nothing about reservations.
        Assert.Equal("google_maps_only", nothingKnown.Status);
        Assert.Null(nothingKnown.PhoneNumber);
        Assert.Contains("query_place_id=place-1", nothingKnown.Url);
    }

    // --- OpenAI transport -----------------------------------------------------------------------------

    [Fact]
    public async Task OpenAiClient_RetriesRateLimitsAndThenSucceeds()
    {
        var handler = new TestHost.StubHttpHandler(
            TestHost.StubHttpHandler.Status(HttpStatusCode.TooManyRequests),
            TestHost.StubHttpHandler.OpenAi("""{"ok":true}"""));
        var client = TestHost.CreateOpenAiClient(handler);

        var json = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(json["ok"]!.GetValue<bool>());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task OpenAiClient_ReportsRateLimitingDistinctlyWhenRetriesAreExhausted()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Status(HttpStatusCode.TooManyRequests));
        var client = TestHost.CreateOpenAiClient(handler, new OpenAIOptions { ApiKey = "k", Model = "m", MaxTransportAttempts = 2 });

        var error = await Assert.ThrowsAsync<ProviderFailureException>(() => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal("reasoning_provider_rate_limited", error.Code);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task OpenAiClient_DoesNotRetryARequestThatCannotSucceed()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.Status(HttpStatusCode.BadRequest));
        var client = TestHost.CreateOpenAiClient(handler);

        var error = await Assert.ThrowsAsync<ProviderFailureException>(() => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal("reasoning_provider_failed", error.Code);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task OpenAiClient_SendsAStrictSchemaAndATokenCeiling()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.OpenAi("""{"ok":true}"""));
        var client = TestHost.CreateOpenAiClient(handler);

        await client.CompleteAsync(Request(), CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"type\":\"json_schema\"", body);
        Assert.Contains("\"strict\":true", body);
        Assert.Contains("\"max_completion_tokens\"", body);
    }

    [Fact]
    public async Task OpenAiClient_CanFallBackToPlainJsonModeWithoutACodeChange()
    {
        var handler = new TestHost.StubHttpHandler(TestHost.StubHttpHandler.OpenAi("""{"ok":true}"""));
        var client = TestHost.CreateOpenAiClient(handler, new OpenAIOptions { ApiKey = "k", Model = "m", UseStructuredOutputs = false });

        await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Contains("\"type\":\"json_object\"", handler.RequestBodies[0]);
        Assert.DoesNotContain("json_schema", handler.RequestBodies[0]);
    }

    // --- Reasoner validation --------------------------------------------------------------------------

    [Fact]
    public void ReasonerValidation_ProducesActionableRepairFeedback()
    {
        var candidates = new[] { TestHost.Candidate("place-1"), TestHost.Candidate("place-2", "Quiet Courtyard") };

        var hallucinated = Assert.Throws<ProviderFailureException>(() =>
            OpenAIRecommendationReasoner.Validate(Reasoning(Recommendation("place-nope", 1)), candidates));
        var duplicateRank = Assert.Throws<ProviderFailureException>(() =>
            OpenAIRecommendationReasoner.Validate(Reasoning(Recommendation("place-1", 1), Recommendation("place-2", 1)), candidates));
        var badScore = Assert.Throws<ProviderFailureException>(() =>
            OpenAIRecommendationReasoner.Validate(Reasoning(Recommendation("place-1", 1, matchScore: 140)), candidates));
        var badConfidence = Assert.Throws<ProviderFailureException>(() =>
            OpenAIRecommendationReasoner.Validate(Reasoning(Recommendation("place-1", 1, confidence: "very high")), candidates));
        var badLanguage = Assert.Throws<ProviderFailureException>(() =>
            OpenAIRecommendationReasoner.Validate(Reasoning("fr", Recommendation("place-1", 1)), candidates));

        // Without feedback the retry is a blind re-roll, which is why every message names the exact problem.
        Assert.Contains("place-nope", hallucinated.RepairFeedback);
        Assert.Contains("candidate list", hallucinated.RepairFeedback);
        Assert.Contains("more than once", duplicateRank.RepairFeedback);
        Assert.Contains("0-100", badScore.RepairFeedback);
        Assert.Contains("very high", badConfidence.RepairFeedback);
        Assert.Contains("en, de, tr", badLanguage.RepairFeedback);
    }

    [Fact]
    public void ReasonerValidation_AcceptsAWellFormedResult()
    {
        var candidates = new[] { TestHost.Candidate("place-1"), TestHost.Candidate("place-2", "Quiet Courtyard") };

        OpenAIRecommendationReasoner.Validate(
            Reasoning(Recommendation("place-1", 1), Recommendation("place-2", 2)),
            candidates);
    }

    private static RecommendationReasoningResult Reasoning(params ReasonedRecommendation[] recommendations) =>
        Reasoning("en", recommendations);

    private static RecommendationReasoningResult Reasoning(string language, params ReasonedRecommendation[] recommendations) =>
        new("Summary.", new Dictionary<string, object> { ["language"] = language }, recommendations);

    private static ReasonedRecommendation Recommendation(string placeId, int rank, int matchScore = 80, string confidence = "high") =>
        new(placeId, rank, "Label", matchScore, "Reason.", ["Fits."], [], ["Dinner"], confidence);

    private static OpenAiJsonRequest Request() =>
        new("reasoning_provider_failed", "We could not complete this recommendation right now.", "system", "user", "test_schema", JsonSchema.Object(("ok", JsonSchema.Bool())), 512);

    private static DiningProfileDto DiningProfile(int distanceMeters) =>
        new(false, false, false, 5, false, false, false, false, distanceMeters);

    private static GoogleLocationResolver CreateLocationResolver(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new GooglePlacesOptions { ApiKey = "key" }),
            TestHost.Cache(),
            NullLogger<GoogleLocationResolver>.Instance);

    private static GooglePlacesProvider CreateSearchProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new GooglePlacesOptions { ApiKey = "key" }),
            new NoopSnapshotStore(),
            TestHost.Cache(),
            NullLogger<GooglePlacesProvider>.Instance);

    private static string SearchPayload(string placeId) => $$"""
    {"places":[{"id":"{{placeId}}","displayName":{"text":"A Place"},"formattedAddress":"Addr","location":{"latitude":36.88,"longitude":30.70},"types":["restaurant"],"primaryType":"restaurant","rating":4.4,"userRatingCount":210}]}
    """;

    private static string DetailsPayload(string placeId) => $$"""
    {"id":"{{placeId}}","displayName":{"text":"A Place"},"formattedAddress":"Addr","location":{"latitude":36.88,"longitude":30.70},"types":["restaurant"],"primaryType":"restaurant","nationalPhoneNumber":"+90 242 000 1122","websiteUri":"https://a.example"}
    """;

    /// <summary>Builds a container so the snapshot store can create its own scope, the way it does in production.</summary>
    private static ServiceProvider BuildScopedProvider(out string databaseName)
    {
        databaseName = Guid.NewGuid().ToString("N");
        var name = databaseName;
        var services = new ServiceCollection();
        services.AddDbContext<DineCueDbContext>(options => options.UseInMemoryDatabase(name));
        return services.BuildServiceProvider();
    }

    private static DineCueDbContext OpenDb(string databaseName) =>
        new(new DbContextOptionsBuilder<DineCueDbContext>().UseInMemoryDatabase(databaseName).Options);

    private sealed class NoopSnapshotStore : IRestaurantSnapshotStore
    {
        public Task<PlaceCandidate?> TryGetFreshAsync(string providerPlaceId, TimeSpan maxAge, CancellationToken cancellationToken) =>
            Task.FromResult<PlaceCandidate?>(null);

        public Task UpsertAsync(IReadOnlyList<PlaceCandidate> candidates, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
