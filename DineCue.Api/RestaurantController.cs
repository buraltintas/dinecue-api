using DineCue.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("restaurants")]
public sealed class RestaurantController(IRestaurantService restaurants) : DineCueControllerBase
{
    [HttpPost("search")]
    [AllowAnonymous]
    [EnableRateLimiting("general")]
    public Task<IReadOnlyList<RestaurantSearchResultDto>> Search(RestaurantSearchRequest request, CancellationToken ct) => restaurants.SearchAsync(request, ct);

    [HttpGet("{placeId}")]
    [AllowAnonymous]
    [EnableRateLimiting("general")]
    public Task<RestaurantDetailsDto> Get(string placeId, CancellationToken ct) => restaurants.GetAsync(placeId, ct);

    [HttpPost("{placeId}/fit-check")]
    [EnableRateLimiting("fit-check")]
    public Task<RestaurantFitCheckResponse> FitCheck(string placeId, RestaurantFitCheckRequest request, CancellationToken ct) => restaurants.FitCheckAsync(UserId, placeId, request, ct);
}
