using System.Text.Json;
using DineCue.Application;
using DineCue.Infrastructure;
using Xunit;

namespace DineCue.Tests;

/// <summary>
/// Pins the exact JSON the dinecue-ui BFF sends against the request records it lands in. The UI is a separate
/// repository, so a renamed or reordered constructor parameter here would only surface as a broken feature in
/// production. These payloads are copied from the BFF route handlers.
/// </summary>
public sealed class BffPayloadCompatibilityTests
{
    // Matches ASP.NET Core's default request-body options.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MenuScanPayload_FromBff_BindsEveryField()
    {
        const string json = """
        {
          "imageObjectNames": ["menu-scans/user/a.jpg", "menu-scans/user/b.jpg"],
          "rawText": "çocuklar var, çok acı istemiyoruz",
          "selectedCues": ["Family", "Quiet"],
          "restaurantPlaceId": "place-1",
          "language": "tr",
          "context": { "partySize": 4, "withKids": true, "mealMoment": "dinner" }
        }
        """;

        var request = JsonSerializer.Deserialize<MenuScanRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal(["menu-scans/user/a.jpg", "menu-scans/user/b.jpg"], request!.ImageObjectNames);
        Assert.Equal("çocuklar var, çok acı istemiyoruz", request.RawText);
        Assert.Equal(["Family", "Quiet"], request.SelectedCues);
        Assert.Equal("place-1", request.RestaurantPlaceId);
        Assert.Equal("tr", request.Language);
        Assert.NotNull(request.Context);
        Assert.Equal(3, request.Context!.Count);
        // The legacy fields the BFF never sends must stay absent rather than picking up stray values.
        Assert.Null(request.ImageUrl);
        Assert.Null(request.ImageBase64);
        Assert.Null(request.OcrText);
        Assert.Null(request.DiningContext);
    }

    [Fact]
    public void MenuScanPayload_WithOnlyImages_LeavesTheOptionalContextNull()
    {
        // The diner adds photos and submits without typing anything: their stored profile carries the context.
        const string json = """{ "imageObjectNames": ["menu-scans/user/a.jpg"], "language": "en" }""";

        var request = JsonSerializer.Deserialize<MenuScanRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Single(request!.ImageObjectNames!);
        Assert.Null(request.RawText);
        Assert.Null(request.Context);
    }

    [Fact]
    public void RestaurantSearchPayload_FromBff_CarriesTheNearbyCue()
    {
        const string json = """
        {
          "query": "restaurant",
          "language": "tr",
          "location": { "mode": "current", "text": null, "lat": 36.88, "lng": 30.7 },
          "cues": ["nearby"]
        }
        """;

        var request = JsonSerializer.Deserialize<RestaurantSearchRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("restaurant", request!.Query);
        // Without this the "near me" flow silently degrades to rating-ranked results.
        Assert.Equal(["nearby"], request.Cues);
        Assert.Equal("current", request.Location?.Mode);
        Assert.Equal(36.88, request.Location?.Lat);
        RequestValidation.RestaurantSearch(request);
    }

    [Fact]
    public void RestaurantSearchPayload_WithoutCues_IsStillValid()
    {
        const string json = """{ "query": "Harbor Meze", "language": "en", "location": { "mode": "text", "text": "Antalya" } }""";

        var request = JsonSerializer.Deserialize<RestaurantSearchRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Null(request!.Cues);
        RequestValidation.RestaurantSearch(request);
    }

    [Fact]
    public void FitCheckPayload_FromBff_CarriesTheIntendedVisitTime()
    {
        const string json = """
        {
          "rawText": "iki aile, dört çocuk",
          "language": "tr",
          "context": { "partySize": 8, "withKids": true, "mealMoment": "dinner", "visitAt": "2026-08-08T18:00:00.000Z" }
        }
        """;

        var request = JsonSerializer.Deserialize<RestaurantFitCheckRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("tr", request!.Language);
        RequestValidation.RestaurantFit(request);
        // visitAt is what makes the place's opening hours actionable, so it has to survive the round trip.
        var visitAt = RestaurantService.ReadVisitAt(request.Context);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T18:00:00Z"), visitAt);
    }

    [Fact]
    public void RecommendationSessionPayload_FromBff_StillBinds()
    {
        // Guards the live flow: this payload shape is what the find form has always sent.
        const string json = """
        {
          "rawText": "Akşam çocukla sakin bir yer",
          "location": { "mode": "map_pin", "text": "Kaleiçi", "lat": 36.88, "lng": 30.7 },
          "selectedCues": ["Dinner", "Family"],
          "language": "tr",
          "context": { "partySize": 3, "withKids": true, "mealMoment": "dinner" }
        }
        """;

        var request = JsonSerializer.Deserialize<RecommendationSessionRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("map_pin", request!.Location?.Mode);
        Assert.Equal(["Dinner", "Family"], request.SelectedCues);
        RequestValidation.Recommendation(request);
    }

    /// <summary>
    /// The response side of the same contract: these are the field names the UI reads off a completed scan
    /// and a fit check. Serializing with the API's own options catches a casing or naming drift.
    /// </summary>
    [Fact]
    public void MenuScanResponse_SerializesTheFieldNamesTheUiReads()
    {
        var response = new MenuScanResponse(
            Guid.NewGuid(), "tr", "Özet",
            [new MenuSectionDto("Ana yemekler", ["Mercimek çorbası"])],
            [new MenuItemDto("Mercimek çorbası", "Açıklama", "Başlangıç", "120 TL", ["gluten"], true, true, false)],
            [new MenuRecommendationDto("Mercimek çorbası", "Neden uygun.", 0.88, [])],
            ["Uyarı."], ["Soru?"],
            "completed", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, "place-1");

        var json = JsonSerializer.Serialize(response, Options);

        foreach (var field in new[]
                 {
                     "menuScanId", "detectedLanguage", "summary", "sections", "items", "recommendedItems",
                     "warnings", "questionsToAskStaff", "status", "currentStep", "errorCode", "errorMessage",
                     "createdAt", "completedAt", "imageCount", "restaurantPlaceId",
                     "itemNames", "priceText", "possibleAllergens", "isKidFriendly", "isVegetarian", "isSpicy",
                     "itemName", "suitabilityScore"
                 })
        {
            Assert.Contains($"\"{field}\"", json);
        }
    }

    [Fact]
    public void FitCheckResponse_SerializesTheFieldNamesTheUiReads()
    {
        var response = new RestaurantFitCheckResponse(
            78, "Uygun görünüyor", "Özet.", ["Artı."], ["Eksi."], ["Soru?"], 0.72,
            new Dictionary<string, object> { ["goodForChildren"] = new Dictionary<string, string> { ["value"] = "true", ["impact"] = "positive" } },
            "Aramak iyi olur.",
            new ReservationDto("phone_only", "phone", "https://maps.example", "+90 242 111 2233", 0.55));

        var json = JsonSerializer.Serialize(response, Options);

        foreach (var field in new[]
                 {
                     "fitScore", "verdict", "summary", "pros", "cons", "questionsToAsk", "confidence",
                     "relevantSignals", "reservationAdvice", "reservation", "phoneNumber", "impact"
                 })
        {
            Assert.Contains($"\"{field}\"", json);
        }
    }

    [Fact]
    public void MenuImageUploadResponse_SerializesTheObjectNameTheUiSendsBack()
    {
        var json = JsonSerializer.Serialize(
            new MenuImageUploadResponse("menu-scans/user/a.jpg", "image/jpeg", 2048, DateTimeOffset.UtcNow),
            Options);

        Assert.Contains("\"objectName\"", json);
        Assert.Contains("\"contentType\"", json);
        Assert.Contains("\"sizeBytes\"", json);
    }

    /// <summary>
    /// Every prompt must describe its own response shape in prose, not only through the strict json_schema
    /// response format. OpenAI:UseStructuredOutputs is a runtime kill switch, and with it off the prompt is
    /// the only place the model learns the field names.
    /// </summary>
    /// <summary>
    /// Every stored preference has to reach the flow that can act on it. The recommendation prompt used to
    /// dump the profile as a raw DTO with no instruction attached, so the model saw a diner's allergies but
    /// was never told to honour them.
    /// </summary>
    [Fact]
    public void RecommendationPrompt_CarriesTheStoredProfileAndTellsTheModelToHonourIt()
    {
        var prompt = OpenAIRecommendationReasoner.BuildPrompt(
            RichIntent(),
            [TestHost.Candidate()],
            repair: false,
            repairFeedback: null);

        // Every taste and dining field the diner can set must be present.
        foreach (var field in new[]
                 {
                     "allergies", "dietaryRestrictions", "dislikedIngredients", "dislikedCuisines",
                     "favoriteCuisines", "favoriteDishes", "drinkPreferences", "spiceTolerance",
                     "sweetSaltyPreference", "usuallyWithKids", "prefersQuietPlaces", "prefersOutdoor",
                     "budgetSensitivity", "likesLocalExperiences", "likesPremiumPlaces", "needsParking",
                     "needsAccessibility"
                 })
        {
            Assert.Contains($"\"{field}\"", prompt, StringComparison.Ordinal);
        }

        // Values, not just field names.
        Assert.Contains("peanut", prompt, StringComparison.Ordinal);
        Assert.Contains("vegetarian", prompt, StringComparison.Ordinal);
        // And the rules that make them binding rather than decorative.
        Assert.Contains("hardRules", prompt, StringComparison.Ordinal);
        Assert.Contains("contradicts a dietary restriction or allergy", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void FitCheckPrompt_CarriesEveryVenueRelevantPreference()
    {
        var prompt = OpenAiRestaurantFitAnalyzer.BuildPrompt(
            new RestaurantFitContext(
                TestHost.Candidate(),
                new RestaurantFitCheckRequest(null, "en", null),
                new ReservationDto("google_maps_only", "google_maps", null, null, 0.5),
                TestHost.Taste(allergies: ["peanut"], dietaryRestrictions: ["vegetarian"]),
                new DiningProfileDto(true, true, true, 4, true, false, true, true, 1800),
                null),
            repairFeedback: null);

        foreach (var field in new[]
                 {
                     "allergies", "dietaryRestrictions", "dislikedCuisines", "favoriteCuisines",
                     "spiceTolerance", "drinkPreferences", "usuallyWithKids", "prefersQuietPlaces",
                     "prefersOutdoor", "budgetSensitivity", "likesLocalExperiences", "likesPremiumPlaces",
                     "needsParking", "needsAccessibility"
                 })
        {
            Assert.Contains($"\"{field}\"", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MenuScanPrompt_CarriesEveryDishRelevantPreference()
    {
        var prompt = OpenAiMenuInterpreter.BuildPrompt(new MenuInterpretationRequest(
            "Mercimek çorbası", [], "tr", null, [],
            new Dictionary<string, object>(),
            TestHost.Taste(allergies: ["peanut"], dietaryRestrictions: ["vegetarian"], dislikedIngredients: ["coriander"]),
            new DiningProfileDto(true, false, false, 3, true, false, false, false, 1800),
            null));

        foreach (var field in new[]
                 {
                     "allergies", "dietaryRestrictions", "dislikedIngredients", "dislikedCuisines",
                     "favoriteCuisines", "favoriteDishes", "spiceTolerance", "sweetSaltyPreference",
                     "drinkPreferences", "usuallyWithKids", "budgetSensitivity"
                 })
        {
            Assert.Contains($"\"{field}\"", prompt, StringComparison.Ordinal);
        }
        Assert.Contains("coriander", prompt, StringComparison.Ordinal);
        Assert.Contains("Never recommend a dish that conflicts", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stored dietary restriction has to shape the candidate pool, not only the ranking: a vegetarian
    /// searching the same pool as everyone else can only be served by whatever generic queries return.
    /// </summary>
    [Fact]
    public void CandidateSearch_UsesStoredDietaryAndCuisinePreferences()
    {
        var vegetarian = RecommendationCandidateSearchPlanner.BuildQueries(
            TestHost.Intent(rawText: "Somewhere for dinner", location: new LocationInput("text", "Antalya", null, null, null)) with
            {
                TasteProfile = TestHost.Taste(dietaryRestrictions: ["vegetarian"])
            });

        Assert.Contains(vegetarian, x => x.Contains("vegetarian", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("vegan", "vegan")]
    [InlineData("vejetaryen", "vegetarian")]
    [InlineData("gluten free", "gluten free")]
    [InlineData("halal", "halal")]
    [InlineData("no pork", "")]
    public void DietaryTerm_MapsOnlyRestrictionsThatChangeWhichVenuesQualify(string restriction, string expected)
    {
        var term = RecommendationCandidateSearchPlanner.DietaryTerm(TestHost.Taste(dietaryRestrictions: [restriction]));

        if (expected.Length == 0) Assert.Equal("", term);
        else Assert.Contains(expected, term, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FavoriteCuisineTerm_YieldsToWhatTheDinerAskedForTonight()
    {
        var taste = TestHost.Taste();
        var withFavorite = taste with { FavoriteCuisines = ["Japanese"] };

        Assert.Contains("Japanese", RecommendationCandidateSearchPlanner.FavoriteCuisineTerm(withFavorite, "somewhere for dinner"));
        // A standing preference must not override tonight's explicit request.
        Assert.Equal("", RecommendationCandidateSearchPlanner.FavoriteCuisineTerm(withFavorite, "japanese ramen tonight"));
        Assert.Equal("", RecommendationCandidateSearchPlanner.FavoriteCuisineTerm(taste, "somewhere for dinner"));
    }

    private static DiningIntent RichIntent() =>
        TestHost.Intent() with
        {
            TasteProfile = TestHost.Taste(allergies: ["peanut"], dietaryRestrictions: ["vegetarian"], dislikedIngredients: ["coriander"]),
            DiningProfile = new DiningProfileDto(true, true, false, 4, true, false, true, false, 1800),
            Profile = new ProfileDto("Ada", "en", "TR", "TRY", "km")
        };

    [Fact]
    public void MenuScanPrompt_DescribesItsResponseShapeInProse()
    {
        var prompt = OpenAiMenuInterpreter.BuildPrompt(new MenuInterpretationRequest(
            "Mercimek çorbası 120 TL", [], "tr", null, [], new Dictionary<string, object>(), null, null, null));

        foreach (var field in new[]
                 {
                     "readable", "detectedLanguage", "ocrText", "summary", "sections", "itemNames",
                     "items", "priceText", "possibleAllergens", "isKidFriendly", "isVegetarian", "isSpicy",
                     "recommendedItems", "itemName", "suitabilityScore", "warnings", "questionsToAskStaff"
                 })
        {
            Assert.Contains(field, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FitCheckPrompt_DescribesItsResponseShapeInProse()
    {
        var prompt = OpenAiRestaurantFitAnalyzer.BuildPrompt(
            new RestaurantFitContext(
                new PlaceCandidate("google_places", "place-1", "A", "Addr", 1, 2, 4.4m, 10, 2, "{}"),
                new RestaurantFitCheckRequest(null, "en", null),
                new ReservationDto("google_maps_only", "google_maps", null, null, 0.5),
                null,
                null,
                null),
            repairFeedback: null);

        foreach (var field in new[]
                 {
                     "fitScore", "verdict", "summary", "pros", "cons", "questionsToAsk",
                     "confidence", "reservationAdvice", "relevantSignals", "impact"
                 })
        {
            Assert.Contains(field, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RecommendationPrompt_DescribesItsResponseShapeInProse()
    {
        var prompt = OpenAIRecommendationReasoner.BuildPrompt(
            TestHost.Intent(),
            [TestHost.Candidate()],
            repair: false,
            repairFeedback: null);

        foreach (var field in new[] { "summary", "interpretedIntent", "recommendations", "placeId", "matchScore", "whyItFits", "watchOut", "bestFor", "confidence" })
        {
            Assert.Contains(field, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MenuScanAcceptedResponse_SerializesTheIdTheUiNavigatesTo()
    {
        var json = JsonSerializer.Serialize(new MenuScanAcceptedResponse(Guid.NewGuid(), "queued", "/menu-scans/x"), Options);

        Assert.Contains("\"menuScanId\"", json);
        Assert.Contains("\"status\"", json);
        Assert.Contains("\"statusUrl\"", json);
    }
}
