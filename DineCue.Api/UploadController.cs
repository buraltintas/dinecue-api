using DineCue.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[Route("uploads")]
public sealed class UploadController(IMenuScanService menuScans) : DineCueControllerBase
{
    /// <summary>
    /// Stores one menu photo and returns the object name to send with a later scan.
    /// Deliberately free of monthly credits: uploading is retry-safe, and a diner photographing three menu
    /// pages should pay one credit for the scan, not one per photo.
    /// </summary>
    [HttpPost("menu-images")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(24 * 1024 * 1024)]
    public async Task<MenuImageUploadResponse> UploadMenuImage([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            throw new ApiException("validation_error", "Attach a menu photo as the file field.", 400, new { field = "file" });

        await using var content = file.OpenReadStream();
        return await menuScans.UploadImageAsync(UserId, content, file.ContentType, file.FileName, ct);
    }
}
