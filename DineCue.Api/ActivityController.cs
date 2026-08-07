using DineCue.Application;
using Microsoft.AspNetCore.Mvc;

namespace DineCue.Api;

[Route("activity")]
public sealed class ActivityController(IActivityService activity) : DineCueControllerBase
{
    /// <summary>
    /// The diner's record of every credit-spending action, newest first. Omit <c>kind</c> for the merged
    /// feed, or pass recommendation, menu_scan, or fit_check to look at one flow on its own.
    /// </summary>
    [HttpGet]
    public Task<IReadOnlyList<ActivityItemDto>> List([FromQuery] string? kind, [FromQuery] int limit = 30, CancellationToken ct = default) =>
        activity.ListAsync(UserId, kind, limit, ct);
}
