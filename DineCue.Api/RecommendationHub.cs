using DineCue.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DineCue.Api;

[Authorize]
public sealed class RecommendationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId() ?? throw new ApiException("unauthorized", "Authentication is required.", StatusCodes.Status401Unauthorized);
        await Groups.AddToGroupAsync(Context.ConnectionId, RecommendationRealtimeGroups.User(userId));
        await base.OnConnectedAsync();
    }
}

internal static class RecommendationRealtimeGroups
{
    public static string User(Guid userId) => $"user:{userId:N}";
}

public sealed class SignalRRecommendationStatusNotifier(IHubContext<RecommendationHub> hub) : IRecommendationStatusNotifier
{
    public Task StatusChangedAsync(Guid userId, RecommendationStatusChanged status, CancellationToken cancellationToken) =>
        hub.Clients.Group(RecommendationRealtimeGroups.User(userId))
            .SendAsync("recommendation.statusChanged", status, cancellationToken);

    public Task CompletedAsync(Guid userId, RecommendationStatusChanged status, CancellationToken cancellationToken) =>
        hub.Clients.Group(RecommendationRealtimeGroups.User(userId))
            .SendAsync("recommendation.completed", status, cancellationToken);

    public Task FailedAsync(Guid userId, RecommendationFailedEvent failure, CancellationToken cancellationToken) =>
        hub.Clients.Group(RecommendationRealtimeGroups.User(userId))
            .SendAsync("recommendation.failed", failure, cancellationToken);
}
