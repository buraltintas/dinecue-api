using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("")]
public sealed class MemoryController(IRecommendationService recommendations, IInteractionEventService events) : DineCueControllerBase
{
    [HttpGet("history")]
    public Task<IReadOnlyList<HistoryItemDto>> History(CancellationToken ct) => recommendations.ListHistoryAsync(UserId, ct);

    [HttpGet("history/{sessionId:guid}")]
    public Task<RecommendationSessionDetailResponse> HistoryItem(Guid sessionId, CancellationToken ct) => recommendations.GetAsync(UserId, sessionId, ct);

    [HttpGet("saved-places")]
    public Task<IReadOnlyList<SavedPlaceDto>> Saved(CancellationToken ct) => recommendations.GetSavedAsync(UserId, ct);

    [HttpPost("recommendations/{id:guid}/save")]
    [EnableRateLimiting("general")]
    public Task<SavedPlaceDto> Save(Guid id, CancellationToken ct) => recommendations.SaveAsync(UserId, id, ct);

    [HttpPost("recommendations/{id:guid}/unsave")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> Unsave(Guid id, CancellationToken ct)
    {
        await recommendations.UnsSaveAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("recommendations/{id:guid}/feedback")]
    [EnableRateLimiting("general")]
    public Task<FeedbackDto> Feedback(Guid id, FeedbackRequest request, CancellationToken ct) => recommendations.UpsertFeedbackAsync(UserId, id, request, ct);

    [HttpPut("recommendations/{id:guid}/feedback")]
    [EnableRateLimiting("general")]
    public Task<FeedbackDto> PutFeedback(Guid id, FeedbackRequest request, CancellationToken ct) => recommendations.UpsertFeedbackAsync(UserId, id, request, ct);

    [HttpPost("recommendations/{id:guid}/share-text")]
    [EnableRateLimiting("general")]
    public Task<ShareTextResponse> Share(Guid id, CancellationToken ct) => recommendations.ShareTextAsync(UserId, id, ct);

    [HttpPost("interaction-events")]
    [EnableRateLimiting("events")]
    public async Task<IActionResult> Track(InteractionEventRequest request, CancellationToken ct)
    {
        await events.TrackAsync(UserId, request, ct);
        return Accepted();
    }
}
