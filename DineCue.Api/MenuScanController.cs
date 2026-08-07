using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("menu-scans")]
public sealed class MenuScanController(IMenuScanService menuScans) : DineCueControllerBase
{
    /// <summary>
    /// Queues a scan and returns 202. Reading menu photos takes long enough that waiting on the request
    /// would be fragile on a phone, so clients poll GET /menu-scans/{id} the same way they poll a
    /// recommendation session.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("menu-scans")]
    public async Task<ActionResult<MenuScanAcceptedResponse>> Create(MenuScanRequest request, CancellationToken ct)
    {
        var accepted = await menuScans.CreateAsync(UserId, request, ct);
        return Accepted(accepted.StatusUrl, accepted);
    }

    [HttpGet("{id:guid}")]
    public Task<MenuScanResponse> Get(Guid id, CancellationToken ct) => menuScans.GetAsync(UserId, id, ct);

    /// <summary>Re-ranks an already-read menu against the diner's current profile. Costs no monthly credit.</summary>
    [HttpPost("{id:guid}/recommend")]
    [EnableRateLimiting("menu-scans")]
    public Task<MenuScanResponse> Recommend(Guid id, CancellationToken ct) => menuScans.RecommendAsync(UserId, id, ct);

    /// <summary>
    /// Streams a stored menu photo back to its owner. The bucket is private and object names are never
    /// exposed to clients, so this ownership-checked endpoint is the only way to read an image.
    /// </summary>
    [HttpGet("{id:guid}/images/{index:int}")]
    public async Task<IActionResult> GetImage(Guid id, int index, CancellationToken ct)
    {
        var image = await menuScans.OpenImageAsync(UserId, id, index, ct);
        if (image == null) return NotFound(new ApiErrorEnvelope(new ApiError("not_found", "This menu photo was not found.", new { })));
        return File(image.Value.Content, image.Value.ContentType);
    }
}
