using DineCue.Api;
using DineCue.Application;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "auth"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("otp-start", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "otp-start"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("otp-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "otp-verify"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
});
builder.Services.AddDineCueInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.GetUserId();
        var db = context.RequestServices.GetRequiredService<DineCueDbContext>();
        var active = await db.Users.AnyAsync(x => x.Id == userId && x.DeletedAt == null, context.RequestAborted);
        if (!active)
            throw new ApiException("account_deleted", "This account is no longer active.", StatusCodes.Status401Unauthorized);
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.Run();

static string PartitionKey(HttpContext context, string policy)
{
    var user = context.User.Identity?.IsAuthenticated == true ? context.User.GetUserId().ToString("N") : null;
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
    return $"{policy}:{user ?? ip}";
}

internal sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, code, message, details) = ex switch
            {
                ApiException api => (api.StatusCode, api.Code, api.Message, api.Details),
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.", null),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "not_found", "The requested resource was not found.", null),
                ArgumentException => (StatusCodes.Status400BadRequest, "validation_error", ex.Message, null),
                InvalidOperationException => (StatusCodes.Status400BadRequest, "invalid_operation", "The request could not be completed.", null),
                _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.", null)
            };
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiError(code, message, details ?? new { })));
        }
    }
}
