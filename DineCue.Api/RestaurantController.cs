using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("restaurants")]
public sealed class RestaurantController(IRestaurantService restaurants) : DineCueControllerBase
{
    // Search and details now hit the paid Places API, so they require a signed-in caller. The UI never
    // called them anonymously, and leaving them open would be an unmetered cost and abuse surface.
    [HttpPost("search")]
    [EnableRateLimiting("general")]
    public Task<IReadOnlyList<RestaurantSearchResultDto>> Search(RestaurantSearchRequest request, CancellationToken ct) => restaurants.SearchAsync(request, ct);

    [HttpGet("{placeId}")]
    [EnableRateLimiting("general")]
    public Task<RestaurantDetailsDto> Get(string placeId, CancellationToken ct) => restaurants.GetAsync(placeId, ct);

    [HttpPost("{placeId}/fit-check")]
    [EnableRateLimiting("fit-check")]
    public Task<RestaurantFitCheckResponse> FitCheck(string placeId, RestaurantFitCheckRequest request, CancellationToken ct) => restaurants.FitCheckAsync(UserId, placeId, request, ct);

    /// <summary>Returns a fit check the diner already paid a credit for, so the answer stays reviewable.</summary>
    [HttpGet("fit-checks/{fitCheckId:guid}")]
    public Task<RestaurantFitCheckResponse> GetFitCheck(Guid fitCheckId, CancellationToken ct) => restaurants.GetFitCheckAsync(UserId, fitCheckId, ct);
}
