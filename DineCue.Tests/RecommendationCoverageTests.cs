using System.Text.Json.Nodes;
using DineCue.Application;
using DineCue.Infrastructure;
using Xunit;

namespace DineCue.Tests;

public sealed class RecommendationCoverageTests
{
    [Fact]
    public void SearchPlanner_BuildsMultipleQueryVariants()
    {
        var intent = Intent(
            rawText: "Akşam çocukla sakin ve çok pahalı olmayan yerel yemek istiyorum.",
            cues: ["family", "quiet", "good-value"],
            context: new Dictionary<string, object> { ["mealMoment"] = "dinner", ["withKids"] = true },
            locationText: "Döşemealtı Antalya");

        var queries = RecommendationCandidateSearchPlanner.BuildQueries(intent);

        Assert.True(queries.Count >= 4);
        Assert.Contains(queries, x => x.Contains("family", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, x => x.Contains("good value", StringComparison.OrdinalIgnoreCase));
        Assert.All(queries, q => Assert.Contains("Döşemealtı Antalya", q));
    }

    [Fact]
    public void SearchPlanner_DeduplicatesByPlaceIdAndKeepsRicherCandidate()
    {
        var sparse = Candidate("place-1", "A", "", null, null, null, "{}");
        var rich = Candidate("place-1", "A", "Address", 4.5m, 120, 2, "{\"googleMapsUri\":\"https://maps.example\"}");
        var other = Candidate("place-2", "B", "Address B", 4.1m, 20, null, "{}");

        var deduped = RecommendationCandidateSearchPlanner.Deduplicate([sparse, rich, other]);

        Assert.Equal(2, deduped.Count);
        Assert.Equal("Address", deduped.Single(x => x.ProviderPlaceId == "place-1").Address);
    }

    [Fact]
    public async Task MockReasoner_TargetsUpToFiveFinalResults()
    {
        var candidates = Enumerable.Range(1, 6).Select(i => Candidate($"place-{i}", $"Place {i}")).ToArray();
        var reasoner = new MockRecommendationReasoner(new MockAiPlaceRanker());

        var result = await reasoner.RankAsync(Intent(), candidates, CancellationToken.None);

        Assert.Equal(5, result.Recommendations.Count);
        Assert.All(result.Recommendations, x => Assert.Contains(candidates, c => c.ProviderPlaceId == x.PlaceId));
    }

    [Fact]
    public async Task SparseResults_DoNotHallucinateExtraPlaces()
    {
        var candidates = new[] { Candidate("place-1", "Place 1"), Candidate("place-2", "Place 2") };
        var reasoner = new MockRecommendationReasoner(new MockAiPlaceRanker());

        var result = await reasoner.RankAsync(Intent(), candidates, CancellationToken.None);

        Assert.Equal(2, result.Recommendations.Count);
        Assert.All(result.Recommendations, x => Assert.Contains(candidates, c => c.ProviderPlaceId == x.PlaceId));
    }

    [Fact]
    public void OpenAiPrompt_ForbidsInventedRestaurants()
    {
        var candidates = new[] { Candidate("real-place-1", "Real Place") };

        var prompt = OpenAIRecommendationReasoner.BuildPrompt(Intent(), candidates, repair: false, repairFeedback: null);
        var json = JsonNode.Parse(prompt);

        Assert.Contains("Do not invent", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("real-place-1", prompt);
        Assert.Contains("string from candidates only", json?["schema"]?["recommendations"]?[0]?["placeId"]?.GetValue<string>());
    }

    [Fact]
    public void CopyValidation_RejectsUnsupportedClaims()
    {
        var candidate = Candidate("place-1", "Place 1", raw: "{\"types\":[\"restaurant\"]}");

        Assert.True(DisplayText.ContainsUnsupportedClaim(candidate, ["quiet family-friendly place where you can order kebab"], [], [], []));
        Assert.True(DisplayText.ContainsUnsupportedClaim(candidate, ["reservation available by phone"], [], [], []));
        Assert.False(DisplayText.ContainsUnsupportedClaim(candidate, ["looks like it may fit a family dinner"], [], [], []));
    }

    private static DiningIntent Intent(
        string rawText = "Dinner with kids",
        string[]? cues = null,
        Dictionary<string, object>? context = null,
        string? locationText = "Antalya") =>
        new(
            Guid.NewGuid(),
            rawText,
            "en",
            cues ?? ["family"],
            context ?? new Dictionary<string, object>(),
            new LocationInput("text", locationText, null, null, null),
            null,
            null,
            null,
            [],
            []);

    private static PlaceCandidate Candidate(
        string id,
        string name,
        string address = "Address",
        decimal? rating = 4.2m,
        int? ratingCount = 10,
        int? priceLevel = 2,
        string raw = "{}") =>
        new("google_places", id, name, address, 36.0, 30.0, rating, ratingCount, priceLevel, raw);
}
