using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("")]
public sealed class RecommendationController(IRecommendationService recommendations) : DineCueControllerBase
{
    [HttpPost("recommendation-sessions")]
    [EnableRateLimiting("recommendations")]
    public async Task<IActionResult> Create(RecommendationSessionRequest request, CancellationToken ct)
    {
        var response = await recommendations.CreateAsync(UserId, request, ct);
        return Accepted(response.StatusUrl, response);
    }

    [HttpGet("recommendation-sessions")]
    public Task<IReadOnlyList<HistoryItemDto>> List(CancellationToken ct) => recommendations.ListHistoryAsync(UserId, ct);

    [HttpGet("recommendation-sessions/{id:guid}")]
    public Task<RecommendationSessionDetailResponse> Get(Guid id, CancellationToken ct) => recommendations.GetAsync(UserId, id, ct);

    [HttpPost("recommendation-sessions/{id:guid}/refine")]
    [EnableRateLimiting("recommendations")]
    public async Task<IActionResult> Refine(Guid id, RefineRecommendationRequest request, CancellationToken ct)
    {
        var response = await recommendations.RefineAsync(UserId, id, request, ct);
        return Accepted(response.StatusUrl, response);
    }
}
