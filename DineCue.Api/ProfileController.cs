using DineCue.Application;
using Microsoft.AspNetCore.Mvc;

namespace DineCue.Api;

[Route("")]
public sealed class ProfileController(IProfileService profiles) : DineCueControllerBase
{
    [HttpGet("profile")]
    public Task<ProfileDto> GetProfile(CancellationToken ct) => profiles.GetProfileAsync(UserId, ct);

    [HttpPut("profile")]
    public Task<ProfileDto> PutProfile(ProfileDto request, CancellationToken ct) => profiles.UpdateProfileAsync(UserId, request, ct);

    [HttpGet("onboarding/status")]
    public Task<OnboardingStatusResponse> OnboardingStatus(CancellationToken ct) => profiles.GetOnboardingAsync(UserId, ct);

    [HttpPost("onboarding/complete")]
    public Task<OnboardingStatusResponse> CompleteOnboarding(CompleteOnboardingRequest request, CancellationToken ct) => profiles.CompleteOnboardingAsync(UserId, request, ct);

    [HttpGet("taste-profile")]
    public Task<TasteProfileDto> GetTaste(CancellationToken ct) => profiles.GetTasteAsync(UserId, ct);

    [HttpPut("taste-profile")]
    public Task<TasteProfileDto> PutTaste(TasteProfileDto request, CancellationToken ct) => profiles.UpdateTasteAsync(UserId, request, ct);

    [HttpGet("dining-profile")]
    public Task<DiningProfileDto> GetDining(CancellationToken ct) => profiles.GetDiningAsync(UserId, ct);

    [HttpPut("dining-profile")]
    public Task<DiningProfileDto> PutDining(DiningProfileDto request, CancellationToken ct) => profiles.UpdateDiningAsync(UserId, request, ct);
}
