using DineCue.Application;
using DineCue.Api;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DineCue API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("DineCueCors", policy =>
    {
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("AllowedOrigins must be configured outside Development.");
        }

        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "http://localhost:8081", "http://127.0.0.1:8081")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("general", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "general"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "auth"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("otp-start", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "otp-start"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("otp-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "otp-verify"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("recommendations", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "recommendations"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("fit-check", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "fit-check"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("menu-scans", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "menu-scans"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("events", context => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(context, "events"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
});
builder.Services.AddDineCueInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<IRecommendationStatusNotifier, SignalRRecommendationStatusNotifier>();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["Pragma"] = "no-cache";
    await next();
});
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("DineCueCors");
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
        {
            throw new ApiException("account_deleted", "This account is no longer active.", StatusCodes.Status401Unauthorized);
        }
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapHub<RecommendationHub>("/hubs/recommendations").RequireAuthorization();
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();
}

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
                InvalidOperationException when ex.Message.StartsWith("quota_exceeded:") => (StatusCodes.Status429TooManyRequests, "quota_exceeded", ex.Message["quota_exceeded:".Length..], null),
                InvalidOperationException => (StatusCodes.Status400BadRequest, "invalid_operation", "The request could not be completed.", null),
                _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.", null)
            };
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiError(code, message, details ?? new { })));
        }
    }
}
