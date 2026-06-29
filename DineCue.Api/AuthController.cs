using DineCue.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineCue.Api;

[ApiController]
[Route("")]
public sealed class AuthController(IAuthService auth) : DineCueControllerBase
{
    [HttpPost("auth/email/start")]
    [AllowAnonymous]
    [EnableRateLimiting("otp-start")]
    public Task<EmailStartResponse> StartEmail(EmailStartRequest request, CancellationToken ct) =>
        auth.StartEmailAsync(request, ct);

    [HttpPost("auth/email/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("otp-verify")]
    public Task<LoginResponse> VerifyEmail(EmailVerifyRequest request, CancellationToken ct) =>
        auth.VerifyEmailAsync(request, ct);

    [HttpPost("auth/google")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public Task<LoginResponse> Google(GoogleLoginRequest request, CancellationToken ct) =>
        auth.GoogleAsync(request, ct);

    [HttpPost("auth/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public Task<LoginResponse> Refresh(RefreshRequest request, CancellationToken ct) =>
        auth.RefreshAsync(request, ct);

    [HttpPost("auth/logout")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(UserId, request, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetMeAsync(UserId, ct);

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        await auth.DeleteMeAsync(UserId, ct);
        return NoContent();
    }
}
