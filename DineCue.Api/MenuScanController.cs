using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("menu-scans")]
public sealed class MenuScanController(IMenuScanService menuScans) : DineCueControllerBase
{
    [HttpPost]
    [EnableRateLimiting("menu-scans")]
    public Task<MenuScanResponse> Create(MenuScanRequest request, CancellationToken ct) => menuScans.CreateAsync(UserId, request, ct);

    [HttpGet("{id:guid}")]
    public Task<MenuScanResponse> Get(Guid id, CancellationToken ct) => menuScans.GetAsync(UserId, id, ct);

    [HttpPost("{id:guid}/recommend")]
    [EnableRateLimiting("menu-scans")]
    public Task<MenuScanResponse> Recommend(Guid id, CancellationToken ct) => menuScans.RecommendAsync(UserId, id, ct);
}
