using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DineCue.Application;
using DineCue.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace DineCue.Infrastructure;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "DineCue";
    public string Audience { get; set; } = "DineCue.Mobile";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 60;
}

public sealed class EmailOtpOptions
{
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public bool ExposeDevOtp { get; set; }
    public int StartLimitPerEmailWindow { get; set; } = 3;
    public int VerifyLimitPerEmailWindow { get; set; } = 8;
    public int EmailWindowMinutes { get; set; } = 15;
}

public sealed class GooglePlacesOptions
{
    public string ApiKey { get; set; } = "";
    public int MaxCandidates { get; set; } = 12;
    public int DefaultRadiusMeters { get; set; } = 2500;
    public int RequestTimeoutSeconds { get; set; } = 12;
}

public sealed class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int RequestTimeoutSeconds { get; set; } = 30;
}

public sealed class RecommendationOptions
{
    public bool UseMockProvider { get; set; }
}

public sealed class QuotasOptions
{
    public int RecommendationDailyFree { get; set; } = 5;
    public int RecommendationDailyPro { get; set; } = 50;
}

public static class DependencyInjection
{
    public static IServiceCollection AddDineCueInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres or ConnectionStrings:DefaultConnection is required.");
        services.AddDbContext<DineCueDbContext>(options => options.UseNpgsql(connectionString));
        services.AddMemoryCache();
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<EmailOtpOptions>(configuration.GetSection("EmailOtp"));
        services.Configure<GooglePlacesOptions>(configuration.GetSection("GooglePlaces"));
        services.Configure<OpenAIOptions>(configuration.GetSection("OpenAI"));
        services.Configure<RecommendationOptions>(configuration.GetSection("Recommendation"));
        services.Configure<QuotasOptions>(configuration.GetSection("Quotas"));

        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt settings are required.");
        if (string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be supplied from user-secrets or environment and be at least 32 characters.");
        var useMockRecommendationProvider = configuration.GetValue<bool>("Recommendation:UseMockProvider");
        if (!useMockRecommendationProvider)
        {
            if (string.IsNullOrWhiteSpace(configuration["GooglePlaces:ApiKey"]))
                throw new InvalidOperationException("Google Places provider is not configured. Set GooglePlaces:ApiKey using user-secrets or environment variables.");
            if (string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]))
                throw new InvalidOperationException("OpenAI provider is not configured. Set OpenAI:ApiKey using user-secrets or environment variables.");
            if (string.IsNullOrWhiteSpace(configuration["OpenAI:Model"]))
                throw new InvalidOperationException("OpenAI provider is not configured. Set OpenAI:Model.");
        }
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(15),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/recommendations"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<RecommendationProcessor>();
        services.AddScoped<IRestaurantService, RestaurantService>();
        services.AddScoped<IMenuScanService, MenuScanService>();
        services.AddScoped<IInteractionEventService, InteractionEventService>();
        services.AddScoped<IQuotaService, QuotaService>();
        services.AddSingleton<IRecommendationJobQueue, InMemoryRecommendationJobQueue>();
        services.AddHostedService<RecommendationProcessingWorker>();
        if (useMockRecommendationProvider)
        {
            services.AddSingleton<IPlaceSearchProvider, MockRecommendationPlaceSearchProvider>();
            services.AddSingleton<IRecommendationReasoner, MockRecommendationReasoner>();
        }
        else
        {
            services.AddHttpClient<GooglePlacesProvider>((sp, client) =>
            {
                var providerOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GooglePlacesOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(providerOptions.RequestTimeoutSeconds, 1, 60));
            });
            services.AddHttpClient<OpenAIRecommendationReasoner>((sp, client) =>
            {
                var providerOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(providerOptions.RequestTimeoutSeconds, 1, 120));
            });
            services.AddScoped<IPlaceSearchProvider>(sp => sp.GetRequiredService<GooglePlacesProvider>());
            services.AddScoped<IRecommendationReasoner>(sp => sp.GetRequiredService<OpenAIRecommendationReasoner>());
        }
        services.AddScoped<ISubscriptionProvider, MockSubscriptionProvider>();
        services.AddScoped<OtpRateLimiter>();
        services.AddSingleton<IEmailSender>(_ => new MockEmailSender(environment));
        services.AddSingleton<IGoogleAuthValidator>(_ => new MockGoogleAuthValidator(environment));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPlacesProvider, MockPlacesProvider>();
        services.AddSingleton<ILocationResolver, MockLocationResolver>();
        services.AddSingleton<IWeatherProvider, MockWeatherProvider>();
        services.AddSingleton<IRouteProvider, MockRouteProvider>();
        services.AddSingleton<IMenuOcrProvider, MockMenuOcrProvider>();
        services.AddSingleton<IAiIntentParser, MockAiIntentParser>();
        services.AddSingleton<IAiPlaceRanker, MockAiPlaceRanker>();
        services.AddSingleton<IAiRestaurantFitAnalyzer, MockAiRestaurantFitAnalyzer>();
        services.AddSingleton<IAiMenuInterpreter, MockAiMenuInterpreter>();
        services.AddSingleton<IReservationLinkResolver, MockReservationLinkResolver>();
        return services;
    }
}

internal static class JsonText
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, Options) ?? fallback; }
        catch { return fallback; }
    }
}

internal static class Mapping
{
    public static UserDto ToDto(this User user) => new(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.PreferredLanguage, user.Country);
    public static ReservationDto ReservationFromJson(string json) => JsonText.Deserialize(json, new ReservationDto("not_found", "unknown", null, null, 0));
    public static RecommendationCardDto ToCard(this RecommendationResult result, RecommendationCandidate candidate) => new(
        result.Id,
        result.Rank,
        result.Title,
        result.Headline,
        candidate.Name,
        candidate.Address,
        result.Vibe,
        result.Summary,
        result.WhyThisPlace,
        JsonText.Deserialize(result.WhatToOrderJson, Array.Empty<string>()),
        result.GoodToKnow,
        JsonText.Deserialize(result.CautionsJson, Array.Empty<string>()),
        result.Confidence,
        ReservationFromJson(result.ReservationJson),
        result.RouteUrl,
        result.ShareText);
}

internal static class SupportedLanguages
{
    public const string Default = "en";
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal) { "en", "de", "tr" };

    public static string NormalizeOrDefault(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return Default;
        return NormalizeRequired(language);
    }

    public static string NormalizeRequired(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        if (normalized == null || !Values.Contains(normalized))
            throw new ApiException(
                "validation_error",
                "Unsupported language. Supported languages are en, de, tr.",
                400,
                new { field = "language", supportedLanguages = Values.Order().ToArray() });
        return normalized;
    }
}

internal static partial class RequestValidation
{
    private static readonly HashSet<string> LocationModes = new(StringComparer.OrdinalIgnoreCase) { "current", "text", "map_pin", "place" };
    private static readonly HashSet<string> EventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RecommendationViewed",
        "RecommendationSaved",
        "RecommendationUnsaved",
        "DirectionsOpened",
        "ReservationOpened",
        "ShareTextGenerated",
        "FeedbackSubmitted",
        "MenuScanCreated",
        "RestaurantFitChecked"
    };

    public static string Email(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 320 || !EmailRegex().IsMatch(normalized))
            throw Invalid("A valid email is required.", new { field = "email" });
        return normalized;
    }

    public static void Otp(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !OtpRegex().IsMatch(code))
            throw Invalid("A valid 6-digit OTP code is required.", new { field = "code" });
    }

    public static void Language(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
            _ = SupportedLanguages.NormalizeRequired(language);
    }

    public static string PreferredLanguageOrDefault(string? preferredLanguage) =>
        SupportedLanguages.NormalizeOrDefault(preferredLanguage);

    public static string PreferredLanguageRequired(string? preferredLanguage) =>
        SupportedLanguages.NormalizeRequired(preferredLanguage);

    public static void Location(LocationInput? location)
    {
        if (location == null) return;
        if (string.IsNullOrWhiteSpace(location.Mode) || !LocationModes.Contains(location.Mode))
            throw Invalid("location.mode must be one of current, text, map_pin, place.", new { field = "location.mode" });
        if (location.Text is { Length: > 200 })
            throw Invalid("location.text is too long.", new { field = "location.text", maxLength = 200 });
        if (location.PlaceId is { Length: > 200 })
            throw Invalid("location.placeId is too long.", new { field = "location.placeId", maxLength = 200 });
        if (location.Lat is < -90 or > 90)
            throw Invalid("latitude must be between -90 and 90.", new { field = "location.lat" });
        if (location.Lng is < -180 or > 180)
            throw Invalid("longitude must be between -180 and 180.", new { field = "location.lng" });
    }

    public static void Recommendation(RecommendationSessionRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.RawText, "rawText", 1, 1200);
        Language(request.Language);
        Location(request.Location);
        ArrayLimit(request.SelectedCues, "selectedCues", 12, 40);
    }

    public static void Refine(RefineRecommendationRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.RawText, "rawText", 1, 800);
        ArrayLimit(request.SelectedCues, "selectedCues", 12, 40);
    }

    public static void RestaurantSearch(RestaurantSearchRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.Query, "query", 1, 200);
        Language(request.Language);
        Location(request.Location);
    }

    public static void RestaurantFit(RestaurantFitCheckRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.RawText, "rawText", 0, 800);
        Language(request.Language);
    }

    public static void MenuScan(MenuScanRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.ImageUrl, "imageUrl", 0, 2048);
        Text(request.ImageBase64, "imageBase64", 0, 250_000);
        Text(request.OcrText, "ocrText", 0, 20_000);
        Text(request.RestaurantPlaceId, "restaurantPlaceId", 0, 200);
        Language(request.Language);
        if (string.IsNullOrWhiteSpace(request.OcrText) && string.IsNullOrWhiteSpace(request.ImageUrl) && string.IsNullOrWhiteSpace(request.ImageBase64))
            throw Invalid("Provide ocrText, imageUrl, or imageBase64.", new { field = "ocrText" });
    }

    public static void Feedback(FeedbackRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        if (request.Rating is < 1 or > 5)
            throw Invalid("rating must be between 1 and 5.", new { field = "rating" });
        Text(request.Note, "note", 0, 1000);
    }

    public static void Profile(ProfileDto request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        Text(request.DisplayName, "displayName", 0, 120);
        Language(request.PreferredLanguage);
        Text(request.Country, "country", 0, 80);
        Text(request.Currency, "currency", 3, 3);
        if (!CurrencyRegex().IsMatch(request.Currency))
            throw Invalid("currency must be a 3-letter code.", new { field = "currency" });
        if (!string.Equals(request.DistanceUnit, "km", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.DistanceUnit, "mi", StringComparison.OrdinalIgnoreCase))
            throw Invalid("distanceUnit must be km or mi.", new { field = "distanceUnit" });
    }

    public static void Taste(TasteProfileDto request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        ArrayLimit(request.FavoriteCuisines, "favoriteCuisines", 30, 80);
        ArrayLimit(request.DislikedCuisines, "dislikedCuisines", 30, 80);
        ArrayLimit(request.FavoriteDishes, "favoriteDishes", 40, 80);
        ArrayLimit(request.DislikedIngredients, "dislikedIngredients", 60, 80);
        ArrayLimit(request.DrinkPreferences, "drinkPreferences", 30, 80);
        ArrayLimit(request.DietaryRestrictions, "dietaryRestrictions", 30, 80);
        ArrayLimit(request.Allergies, "allergies", 30, 80);
        Text(request.SweetSaltyPreference, "sweetSaltyPreference", 0, 40);
        if (request.SpiceTolerance is < 0 or > 5)
            throw Invalid("spiceTolerance must be between 0 and 5.", new { field = "spiceTolerance" });
    }

    public static void Dining(DiningProfileDto request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        if (request.BudgetSensitivity is < 0 or > 5)
            throw Invalid("budgetSensitivity must be between 0 and 5.", new { field = "budgetSensitivity" });
        if (request.DefaultDistanceMeters is < 100 or > 50_000)
            throw Invalid("defaultDistanceMeters must be between 100 and 50000.", new { field = "defaultDistanceMeters" });
    }

    public static void Interaction(InteractionEventRequest request)
    {
        if (request == null) throw Invalid("Request body is required.", new { field = "body" });
        if (string.IsNullOrWhiteSpace(request.EventType) || !EventTypes.Contains(request.EventType))
            throw Invalid("eventType is not allowed.", new { field = "eventType" });
        Text(request.EntityType, "entityType", 0, 80);
        Text(request.EntityId, "entityId", 0, 200);
        if (request.Metadata is { Count: > 20 })
            throw Invalid("metadata has too many fields.", new { field = "metadata", maxCount = 20 });
        if (request.Metadata != null)
        {
            foreach (var pair in request.Metadata)
            {
                Text(pair.Key, "metadata.key", 1, 80);
                if (pair.Value?.ToString() is { Length: > 500 })
                    throw Invalid("metadata value is too long.", new { field = "metadata", maxLength = 500 });
            }
        }
    }

    private static void ArrayLimit(string[]? values, string field, int maxCount, int maxItemLength)
    {
        if (values == null) return;
        if (values.Length > maxCount)
            throw Invalid($"{field} has too many items.", new { field, maxCount });
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
                throw Invalid($"{field} contains an empty item.", new { field });
            Text(item, field, 1, maxItemLength);
        }
    }

    private static void Text(string? value, string field, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (minLength > 0) throw Invalid($"{field} is required.", new { field });
            return;
        }
        var length = value.Trim().Length;
        if (length < minLength || length > maxLength)
            throw Invalid($"{field} length is invalid.", new { field, minLength, maxLength });
    }

    private static ApiException Invalid(string message, object details) => new("validation_error", message, 400, details);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"^\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex OtpRegex();
    [GeneratedRegex(@"^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyRegex();
}

internal sealed class OtpRateLimiter(IMemoryCache cache, Microsoft.Extensions.Options.IOptions<EmailOtpOptions> options)
{
    public void CheckStart(string email) => Check($"otp-start:{email}", options.Value.StartLimitPerEmailWindow);
    public void CheckVerify(string email) => Check($"otp-verify:{email}", options.Value.VerifyLimitPerEmailWindow);

    private void Check(string key, int limit)
    {
        var window = TimeSpan.FromMinutes(Math.Clamp(options.Value.EmailWindowMinutes, 1, 60));
        var count = cache.Get<int?>(key) ?? 0;
        if (count >= limit)
            throw new ApiException("rate_limited", "Too many attempts. Please try again later.", 429);
        cache.Set(key, count + 1, window);
    }
}

internal sealed class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public string CreateAccessToken(Guid userId, string email, string preferredLanguage)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("lang", preferredLanguage)
        };
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public string HashOtp(string email, string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{email}:{code}:{_options.SigningKey}"))).ToLowerInvariant();
}

internal sealed class AuthService(
    DineCueDbContext db,
    ITokenService tokens,
    IGoogleAuthValidator google,
    IEmailSender emailSender,
    OtpRateLimiter otpRateLimiter,
    IHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<EmailOtpOptions> otpOptions,
    Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions) : IAuthService
{
    public async Task<EmailStartResponse> StartEmailAsync(EmailStartRequest request, CancellationToken ct)
    {
        if (request == null) throw new ApiException("validation_error", "Request body is required.", 400, new { field = "body" });
        var email = NormalizeEmail(request.Email);
        _ = RequestValidation.PreferredLanguageOrDefault(request.PreferredLanguage);
        otpRateLimiter.CheckStart(email);
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var now = DateTimeOffset.UtcNow;
        await db.EmailOtps.Where(x => x.Email == email && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.ConsumedAt, now), ct);
        db.EmailOtps.Add(new EmailOtp
        {
            Email = email,
            CodeHash = tokens.HashOtp(email, code),
            ExpiresAt = now.AddMinutes(otpOptions.Value.ExpiryMinutes),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await emailSender.SendOtpAsync(email, code, ct);
        return new EmailStartResponse("If the email can receive a code, an OTP has been sent.", environment.IsDevelopment() && otpOptions.Value.ExposeDevOtp ? code : null);
    }

    public async Task<LoginResponse> VerifyEmailAsync(EmailVerifyRequest request, CancellationToken ct)
    {
        if (request == null) throw new ApiException("validation_error", "Request body is required.", 400, new { field = "body" });
        var email = NormalizeEmail(request.Email);
        var preferredLanguage = RequestValidation.PreferredLanguageOrDefault(request.PreferredLanguage);
        RequestValidation.Otp(request.Code);
        otpRateLimiter.CheckVerify(email);
        var otp = await db.EmailOtps.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(x => x.Email == email && x.ConsumedAt == null, ct)
            ?? throw new ApiException("invalid_otp", "The OTP is invalid or expired.", 400);
        if (otp.ExpiresAt <= DateTimeOffset.UtcNow) throw new ApiException("invalid_otp", "The OTP is invalid or expired.", 400);
        if (otp.AttemptCount >= otpOptions.Value.MaxAttempts) throw new ApiException("otp_locked", "Too many OTP attempts.", 429);
        if (otp.CodeHash != tokens.HashOtp(email, request.Code))
        {
            otp.AttemptCount++;
            await db.SaveChangesAsync(ct);
            throw new ApiException("invalid_otp", "The OTP is invalid or expired.", 400);
        }
        return await SignInAsync("email", email, email, null, null, preferredLanguage, otp, ct);
    }

    public async Task<LoginResponse> GoogleAsync(GoogleLoginRequest request, CancellationToken ct)
    {
        if (request == null) throw new ApiException("validation_error", "Request body is required.", 400, new { field = "body" });
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096)
            throw new ApiException("validation_error", "A valid Google token is required.", 400, new { field = "token" });
        var preferredLanguage = RequestValidation.PreferredLanguageOrDefault(request.PreferredLanguage);
        var info = await google.ValidateAsync(request.Token, ct);
        return await SignInAsync("google", info.ProviderUserId, NormalizeEmail(info.Email), info.DisplayName, info.AvatarUrl, preferredLanguage, null, ct);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        if (request == null) throw new ApiException("validation_error", "Request body is required.", 400, new { field = "body" });
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 500)
            throw new ApiException("invalid_refresh_token", "Refresh token is invalid.", 401);
        var tokenHash = tokens.HashToken(request.RefreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct)
            ?? throw new ApiException("invalid_refresh_token", "Refresh token is invalid.", 401);
        if (existing.RevokedAt != null)
        {
            await RevokeRefreshTokenChainAsync(existing.UserId, ct);
            throw new ApiException("invalid_refresh_token", "Refresh token is invalid.", 401);
        }
        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ApiException("invalid_refresh_token", "Refresh token is invalid.", 401);
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == existing.UserId, ct)
            ?? throw new ApiException("invalid_refresh_token", "Refresh token is invalid.", 401);
        var refresh = tokens.CreateRefreshToken();
        var newToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(refresh),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        db.RefreshTokens.Add(newToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newToken.Id;
        await db.SaveChangesAsync(ct);
        return new LoginResponse(tokens.CreateAccessToken(user.Id, user.Email, user.PreferredLanguage), refresh, user.ToDto(), false, user.OnboardingCompletedAt != null);
    }

    public async Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken ct)
    {
        if (request == null)
            return;
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 500)
            return;
        var hash = tokens.HashToken(request.RefreshToken);
        await db.RefreshTokens.Where(x => x.UserId == userId && x.TokenHash == hash && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct) =>
        (await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct) ?? throw new ApiException("not_found", "User was not found.", 404)).ToDto();

    public async Task DeleteMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct) ?? throw new ApiException("not_found", "User was not found.", 404);
        user.DeletedAt = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<LoginResponse> SignInAsync(string provider, string providerUserId, string email, string? displayName, string? avatarUrl, string preferredLanguage, EmailOtp? consumedOtp, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var isNew = false;
            if (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == email && x.DeletedAt != null, ct))
                throw new ApiException("account_deleted", "This account is no longer active.", 401);

            var identity = await db.UserIdentities.FirstOrDefaultAsync(x => x.Provider == provider && x.ProviderUserId == providerUserId, ct);
            User? user = null;
            if (identity != null) user = await db.Users.FirstOrDefaultAsync(x => x.Id == identity.UserId, ct);
            user ??= await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);

            if (user == null)
            {
                isNew = true;
                user = new User { Email = email, DisplayName = displayName, AvatarUrl = avatarUrl, PreferredLanguage = preferredLanguage };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            user.DisplayName ??= displayName;
            user.AvatarUrl ??= avatarUrl;
            user.FirstLoginAt ??= DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
            user.LoginCount++;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            if (identity == null)
                db.UserIdentities.Add(new UserIdentity { UserId = user.Id, Provider = provider, ProviderUserId = providerUserId, Email = email });

            await EnsureUserDefaultsAsync(user, displayName, isNew ? preferredLanguage : user.PreferredLanguage, ct);

            var refresh = tokens.CreateRefreshToken();
            db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = tokens.HashToken(refresh), ExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays) });
            if (consumedOtp != null)
                consumedOtp.ConsumedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new LoginResponse(tokens.CreateAccessToken(user.Id, user.Email, user.PreferredLanguage), refresh, user.ToDto(), isNew, user.OnboardingCompletedAt != null);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task EnsureUserDefaultsAsync(User user, string? displayName, string preferredLanguage, CancellationToken ct)
    {
        if (!await db.UserProfiles.AnyAsync(x => x.UserId == user.Id, ct))
            db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = displayName ?? user.DisplayName, PreferredLanguage = preferredLanguage });
        if (!await db.TasteProfiles.AnyAsync(x => x.UserId == user.Id, ct))
            db.TasteProfiles.Add(new TasteProfile { UserId = user.Id });
        if (!await db.DiningProfiles.AnyAsync(x => x.UserId == user.Id, ct))
            db.DiningProfiles.Add(new DiningProfile { UserId = user.Id });
        if (!await db.OnboardingStates.AnyAsync(x => x.UserId == user.Id, ct))
            db.OnboardingStates.Add(new OnboardingState { UserId = user.Id });
    }

    private static string NormalizeEmail(string email)
    {
        return RequestValidation.Email(email);
    }

    private async Task RevokeRefreshTokenChainAsync(Guid userId, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }
}

internal sealed class ProfileService(DineCueDbContext db) : IProfileService
{
    public async Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken ct) => ToDto(await EnsureProfile(userId, ct));
    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileDto request, CancellationToken ct)
    {
        RequestValidation.Profile(request);
        var preferredLanguage = RequestValidation.PreferredLanguageRequired(request.PreferredLanguage);
        var profile = await EnsureProfile(userId, ct);
        profile.DisplayName = request.DisplayName;
        profile.PreferredLanguage = preferredLanguage;
        profile.Country = request.Country;
        profile.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency;
        profile.DistanceUnit = string.IsNullOrWhiteSpace(request.DistanceUnit) ? "km" : request.DistanceUnit;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        var user = await db.Users.FirstAsync(x => x.Id == userId, ct);
        user.DisplayName = profile.DisplayName;
        user.PreferredLanguage = preferredLanguage;
        user.Country = profile.Country;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(profile);
    }
    public async Task<OnboardingStatusResponse> GetOnboardingAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(x => x.Id == userId, ct);
        var state = await EnsureOnboarding(userId, ct);
        return new(user.OnboardingCompletedAt != null, user.OnboardingCompletedAt, JsonText.Deserialize(state.CompletedStepsJson, Array.Empty<string>()));
    }
    public async Task<OnboardingStatusResponse> CompleteOnboardingAsync(Guid userId, CompleteOnboardingRequest request, CancellationToken ct)
    {
        if (request == null) throw new ApiException("validation_error", "Request body is required.", 400, new { field = "body" });
        if (request.CompletedSteps == null || request.CompletedSteps.Length > 20 || request.CompletedSteps.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
            throw new ApiException("validation_error", "completedSteps is invalid.", 400, new { field = "completedSteps" });
        var user = await db.Users.FirstAsync(x => x.Id == userId, ct);
        var state = await EnsureOnboarding(userId, ct);
        user.OnboardingCompletedAt ??= DateTimeOffset.UtcNow;
        state.CompletedStepsJson = JsonText.Serialize(request.CompletedSteps.Length == 0 ? new[] { "profile", "taste", "dining" } : request.CompletedSteps);
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(true, user.OnboardingCompletedAt, JsonText.Deserialize(state.CompletedStepsJson, Array.Empty<string>()));
    }
    public async Task<TasteProfileDto> GetTasteAsync(Guid userId, CancellationToken ct) => ToDto(await EnsureTaste(userId, ct));
    public async Task<TasteProfileDto> UpdateTasteAsync(Guid userId, TasteProfileDto request, CancellationToken ct)
    {
        RequestValidation.Taste(request);
        var taste = await EnsureTaste(userId, ct);
        taste.FavoriteCuisinesJson = JsonText.Serialize(request.FavoriteCuisines ?? []);
        taste.DislikedCuisinesJson = JsonText.Serialize(request.DislikedCuisines ?? []);
        taste.FavoriteDishesJson = JsonText.Serialize(request.FavoriteDishes ?? []);
        taste.DislikedIngredientsJson = JsonText.Serialize(request.DislikedIngredients ?? []);
        taste.SpiceTolerance = Math.Clamp(request.SpiceTolerance, 0, 5);
        taste.SweetSaltyPreference = request.SweetSaltyPreference;
        taste.DrinkPreferencesJson = JsonText.Serialize(request.DrinkPreferences ?? []);
        taste.DietaryRestrictionsJson = JsonText.Serialize(request.DietaryRestrictions ?? []);
        taste.AllergiesJson = JsonText.Serialize(request.Allergies ?? []);
        taste.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(taste);
    }
    public async Task<DiningProfileDto> GetDiningAsync(Guid userId, CancellationToken ct) => ToDto(await EnsureDining(userId, ct));
    public async Task<DiningProfileDto> UpdateDiningAsync(Guid userId, DiningProfileDto request, CancellationToken ct)
    {
        RequestValidation.Dining(request);
        var dining = await EnsureDining(userId, ct);
        dining.UsuallyWithKids = request.UsuallyWithKids;
        dining.PrefersQuietPlaces = request.PrefersQuietPlaces;
        dining.PrefersOutdoor = request.PrefersOutdoor;
        dining.BudgetSensitivity = Math.Clamp(request.BudgetSensitivity, 0, 5);
        dining.LikesLocalExperiences = request.LikesLocalExperiences;
        dining.LikesPremiumPlaces = request.LikesPremiumPlaces;
        dining.NeedsParking = request.NeedsParking;
        dining.NeedsAccessibility = request.NeedsAccessibility;
        dining.DefaultDistanceMeters = request.DefaultDistanceMeters <= 0 ? 1800 : request.DefaultDistanceMeters;
        dining.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(dining);
    }
    private async Task<UserProfile> EnsureProfile(Guid id, CancellationToken ct) => await db.UserProfiles.FindAsync([id], ct) ?? Add(new UserProfile { UserId = id });
    private async Task<TasteProfile> EnsureTaste(Guid id, CancellationToken ct) => await db.TasteProfiles.FindAsync([id], ct) ?? Add(new TasteProfile { UserId = id });
    private async Task<DiningProfile> EnsureDining(Guid id, CancellationToken ct) => await db.DiningProfiles.FindAsync([id], ct) ?? Add(new DiningProfile { UserId = id });
    private async Task<OnboardingState> EnsureOnboarding(Guid id, CancellationToken ct) => await db.OnboardingStates.FindAsync([id], ct) ?? Add(new OnboardingState { UserId = id });
    private T Add<T>(T value) where T : class { db.Add(value); return value; }
    private static ProfileDto ToDto(UserProfile x) => new(x.DisplayName, x.PreferredLanguage, x.Country, x.Currency, x.DistanceUnit);
    private static TasteProfileDto ToDto(TasteProfile x) => new(JsonText.Deserialize(x.FavoriteCuisinesJson, Array.Empty<string>()), JsonText.Deserialize(x.DislikedCuisinesJson, Array.Empty<string>()), JsonText.Deserialize(x.FavoriteDishesJson, Array.Empty<string>()), JsonText.Deserialize(x.DislikedIngredientsJson, Array.Empty<string>()), x.SpiceTolerance, x.SweetSaltyPreference, JsonText.Deserialize(x.DrinkPreferencesJson, Array.Empty<string>()), JsonText.Deserialize(x.DietaryRestrictionsJson, Array.Empty<string>()), JsonText.Deserialize(x.AllergiesJson, Array.Empty<string>()));
    private static DiningProfileDto ToDto(DiningProfile x) => new(x.UsuallyWithKids, x.PrefersQuietPlaces, x.PrefersOutdoor, x.BudgetSensitivity, x.LikesLocalExperiences, x.LikesPremiumPlaces, x.NeedsParking, x.NeedsAccessibility, x.DefaultDistanceMeters);
}

internal sealed class QuotaService(DineCueDbContext db, ISubscriptionProvider subscriptions, Microsoft.Extensions.Options.IOptions<QuotasOptions> options) : IQuotaService
{
    public async Task CheckAndConsumeRecommendationAsync(Guid userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pro = await subscriptions.HasActiveProAsync(userId, ct);
        var quotaOptions = options.Value;
        var limit = Math.Max(1, pro ? quotaOptions.RecommendationDailyPro : quotaOptions.RecommendationDailyFree);
        var usage = await db.DailyUsages.FirstOrDefaultAsync(x => x.UserId == userId && x.UsageDate == today, ct);
        if (usage == null)
        {
            usage = new DailyUsage { UserId = userId, UsageDate = today };
            db.DailyUsages.Add(usage);
        }
        if (usage.RecommendationSessionCount >= limit)
            throw new ApiException("quota_exceeded", "You have reached your daily recommendation limit.", 429, new { limit, plan = pro ? "pro" : "free" });
        usage.RecommendationSessionCount++;
        usage.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class RecommendationService(
    DineCueDbContext db,
    IQuotaService quota,
    IRecommendationJobQueue jobQueue,
    IInteractionEventService events) : IRecommendationService
{
    public async Task<RecommendationSessionAcceptedResponse> CreateAsync(Guid userId, RecommendationSessionRequest request, CancellationToken ct)
    {
        RequestValidation.Recommendation(request);
        var language = SupportedLanguages.NormalizeOrDefault(request.Language);
        await quota.CheckAndConsumeRecommendationAsync(userId, ct);
        var cues = request.SelectedCues ?? [];
        var location = request.Location ?? new LocationInput("current", null, null, null, null);

        var session = new RecommendationSession
        {
            UserId = userId,
            RawText = request.RawText,
            Language = language,
            LocationMode = string.IsNullOrWhiteSpace(location.Mode) ? "current" : location.Mode.Trim().ToLowerInvariant(),
            LocationText = location.Text,
            Latitude = location.Lat,
            Longitude = location.Lng,
            PlaceId = location.PlaceId,
            SelectedCuesJson = JsonText.Serialize(cues),
            InputContextJson = JsonText.Serialize(request.Context ?? new Dictionary<string, object>()),
            Status = "queued"
        };
        db.RecommendationSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await jobQueue.EnqueueAsync(new RecommendationJob(session.Id, userId), ct);
        return new RecommendationSessionAcceptedResponse(session.Id, session.Status, $"/recommendation-sessions/{session.Id}");
    }

    public async Task<IReadOnlyList<HistoryItemDto>> ListHistoryAsync(Guid userId, CancellationToken ct)
    {
        var sessions = await db.RecommendationSessions.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToArray();
        var candidates = await db.RecommendationCandidates.AsNoTracking().Where(x => ids.Contains(x.SessionId)).ToListAsync(ct);
        var results = await db.RecommendationResults.AsNoTracking().Where(x => ids.Contains(x.SessionId)).OrderBy(x => x.Rank).ToListAsync(ct);
        return sessions.Select(s => ToHistory(s, candidates, results)).ToList();
    }

    public async Task<RecommendationSessionDetailResponse> GetAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.RecommendationSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct)
            ?? throw new ApiException("not_found", "Recommendation session was not found.", 404);
        var candidates = await db.RecommendationCandidates.AsNoTracking().Where(x => x.SessionId == sessionId).ToListAsync(ct);
        var results = await db.RecommendationResults.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Rank).ToListAsync(ct);
        var cards = results.Select(r => r.ToCard(candidates.First(c => c.Id == r.CandidateId))).ToList();
        return new RecommendationSessionDetailResponse(
            session.Id,
            session.Status,
            session.CurrentStep,
            session.RawText,
            session.Language,
            session.LocationMode,
            session.LocationText,
            session.CreatedAt,
            session.StartedAt,
            session.CompletedAt,
            session.FailedAt,
            session.ErrorCode,
            session.ErrorMessage,
            JsonText.Deserialize(session.NormalizedContextJson, new Dictionary<string, object>()),
            JsonText.Deserialize(session.AssumptionsJson, new Dictionary<string, object>()),
            cards);
    }

    public async Task<RecommendationSessionAcceptedResponse> RefineAsync(Guid userId, Guid sessionId, RefineRecommendationRequest request, CancellationToken ct)
    {
        RequestValidation.Refine(request);
        var previous = await db.RecommendationSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct)
            ?? throw new ApiException("not_found", "Recommendation session was not found.", 404);
        return await CreateAsync(userId, new RecommendationSessionRequest(
            $"{previous.RawText}\nRefinement: {request.RawText}",
            new LocationInput(previous.LocationMode, previous.LocationText, previous.Latitude, previous.Longitude, previous.PlaceId),
            request.SelectedCues,
            previous.Language,
            request.Context), ct);
    }

    public async Task<SavedPlaceDto> SaveAsync(Guid userId, Guid recommendationResultId, CancellationToken ct)
    {
        var pair = await ResultWithCandidate(userId, recommendationResultId, ct);
        var saved = await db.SavedPlaces.FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderPlaceId == pair.Candidate.ProviderPlaceId, ct);
        if (saved == null)
        {
            saved = new SavedPlace { UserId = userId, Provider = pair.Candidate.Provider, ProviderPlaceId = pair.Candidate.ProviderPlaceId, RecommendationResultId = recommendationResultId, Name = pair.Candidate.Name, Address = pair.Candidate.Address };
            db.SavedPlaces.Add(saved);
        }
        await events.TrackAsync(userId, new InteractionEventRequest("RecommendationSaved", "RecommendationResult", recommendationResultId.ToString(), null), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(saved);
    }

    public async Task UnsSaveAsync(Guid userId, Guid recommendationResultId, CancellationToken ct)
    {
        var saved = await db.SavedPlaces.FirstOrDefaultAsync(x => x.UserId == userId && x.RecommendationResultId == recommendationResultId, ct);
        if (saved != null) db.SavedPlaces.Remove(saved);
        await events.TrackAsync(userId, new InteractionEventRequest("RecommendationUnsaved", "RecommendationResult", recommendationResultId.ToString(), null), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SavedPlaceDto>> GetSavedAsync(Guid userId, CancellationToken ct)
    {
        var saved = await db.SavedPlaces.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return saved.Select(ToDto).ToList();
    }

    public async Task<FeedbackDto> UpsertFeedbackAsync(Guid userId, Guid recommendationResultId, FeedbackRequest request, CancellationToken ct)
    {
        RequestValidation.Feedback(request);
        _ = await ResultWithCandidate(userId, recommendationResultId, ct);
        var feedback = await db.RecommendationFeedback.FirstOrDefaultAsync(x => x.UserId == userId && x.RecommendationResultId == recommendationResultId, ct);
        if (feedback == null)
        {
            feedback = new RecommendationFeedback { UserId = userId, RecommendationResultId = recommendationResultId };
            db.RecommendationFeedback.Add(feedback);
        }
        feedback.Went = request.Went;
        feedback.Liked = request.Liked;
        feedback.Rating = request.Rating is null ? null : Math.Clamp(request.Rating.Value, 1, 5);
        feedback.WouldGoAgain = request.WouldGoAgain;
        feedback.Note = request.Note;
        feedback.UpdatedAt = DateTimeOffset.UtcNow;
        await events.TrackAsync(userId, new InteractionEventRequest("FeedbackSubmitted", "RecommendationResult", recommendationResultId.ToString(), null), ct);
        await db.SaveChangesAsync(ct);
        return new(feedback.Id, feedback.RecommendationResultId, feedback.Went, feedback.Liked, feedback.Rating, feedback.WouldGoAgain, feedback.Note);
    }

    public async Task<ShareTextResponse> ShareTextAsync(Guid userId, Guid recommendationResultId, CancellationToken ct)
    {
        var pair = await ResultWithCandidate(userId, recommendationResultId, ct);
        var text = $"{pair.Candidate.Name} looks like a good fit: {pair.Result.Headline}. {pair.Result.RouteUrl}";
        await events.TrackAsync(userId, new InteractionEventRequest("ShareTextGenerated", "RecommendationResult", recommendationResultId.ToString(), null), ct);
        await db.SaveChangesAsync(ct);
        return new ShareTextResponse(text);
    }

    private async Task<(RecommendationResult Result, RecommendationCandidate Candidate)> ResultWithCandidate(Guid userId, Guid resultId, CancellationToken ct)
    {
        var result = await db.RecommendationResults.FirstOrDefaultAsync(x => x.Id == resultId, ct) ?? throw new ApiException("not_found", "Recommendation was not found.", 404);
        var session = await db.RecommendationSessions.FirstOrDefaultAsync(x => x.Id == result.SessionId && x.UserId == userId, ct) ?? throw new ApiException("not_found", "Recommendation was not found.", 404);
        var candidate = await db.RecommendationCandidates.FirstAsync(x => x.Id == result.CandidateId, ct);
        return (result, candidate);
    }

    private static HistoryItemDto ToHistory(RecommendationSession session, List<RecommendationCandidate> candidates, List<RecommendationResult> results)
    {
        var cards = results.Where(x => x.SessionId == session.Id)
            .Select(r => r.ToCard(candidates.First(c => c.Id == r.CandidateId)))
            .ToList();
        return new HistoryItemDto(session.Id, session.RawText, session.LocationMode, session.LocationText, session.CreatedAt, cards);
    }
    private static SavedPlaceDto ToDto(SavedPlace x) => new(x.Id, x.Provider, x.ProviderPlaceId, x.RecommendationResultId, x.Name, x.Address, x.Note, x.CreatedAt);
}

internal sealed class RecommendationProcessor(
    DineCueDbContext db,
    IAiIntentParser intentParser,
    ILocationResolver locations,
    IWeatherProvider weatherProvider,
    IPlaceSearchProvider placeSearchProvider,
    IRecommendationReasoner reasoner,
    IReservationLinkResolver reservations,
    IRouteProvider routes,
    IInteractionEventService events,
    IRecommendationStatusNotifier notifier,
    ILogger<RecommendationProcessor> logger,
    Microsoft.Extensions.Options.IOptions<RecommendationOptions> recommendationOptions)
{
    public async Task ProcessAsync(RecommendationJob job, CancellationToken ct)
    {
        var session = await db.RecommendationSessions.FirstOrDefaultAsync(x => x.Id == job.SessionId && x.UserId == job.UserId, ct);
        if (session == null) return;
        if (session.Status is "completed" or "running" or "cancelled") return;

        try
        {
            await MarkRunningAsync(session, "understanding_request", ct);
            var cues = JsonText.Deserialize(session.SelectedCuesJson, Array.Empty<string>());
            var context = JsonText.Deserialize(session.InputContextJson, new Dictionary<string, object>());
            var intent = await intentParser.ParseAsync(session.RawText, cues, context, ct);
            var diningProfile = await db.DiningProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == session.UserId, ct);
            ApplyFamilyContextSignals(intent.NormalizedContext, cues, context, diningProfile?.UsuallyWithKids ?? false);
            var originalLocation = new LocationInput(session.LocationMode, session.LocationText, session.Latitude, session.Longitude, session.PlaceId);
            var hasRealOrigin = originalLocation.Lat is not null && originalLocation.Lng is not null;
            var locationConfidence = LocationConfidence(originalLocation);
            NormalizeLocationAssumptions(intent.Assumptions, locationConfidence, recommendationOptions.Value.UseMockProvider);

            session.NormalizedContextJson = JsonText.Serialize(intent.NormalizedContext);
            session.AssumptionsJson = JsonText.Serialize(intent.Assumptions);
            await db.SaveChangesAsync(ct);
            await notifier.StatusChangedAsync(session.UserId, new RecommendationStatusChanged(session.Id, session.Status, session.CurrentStep), ct);

            await SetStepAsync(session, "searching_places", ct);
            var location = await locations.ResolveAsync(originalLocation, ct);
            foreach (var assumption in location.Assumptions) intent.Assumptions.TryAdd(assumption.Key, assumption.Value);
            NormalizeLocationAssumptions(intent.Assumptions, locationConfidence, recommendationOptions.Value.UseMockProvider);
            session.LocationMode = location.Mode;
            session.LocationText = location.Text;
            session.Latitude = hasRealOrigin || recommendationOptions.Value.UseMockProvider ? location.Latitude : null;
            session.Longitude = hasRealOrigin || recommendationOptions.Value.UseMockProvider ? location.Longitude : null;
            session.PlaceId = location.PlaceId;
            session.AssumptionsJson = JsonText.Serialize(intent.Assumptions);

            var weather = await weatherProvider.GetCurrentAsync(location.Latitude, location.Longitude, ct);
            session.WeatherContextJson = JsonText.Serialize(weather);
            var providerLocation = hasRealOrigin || recommendationOptions.Value.UseMockProvider
                ? new LocationInput(location.Mode, location.Text, location.Latitude, location.Longitude, location.PlaceId)
                : new LocationInput(location.Mode, location.Text, null, null, location.PlaceId);
            var diningIntent = await BuildDiningIntentAsync(session, cues, context, providerLocation, ct);
            var candidates = await placeSearchProvider.SearchAsync(diningIntent, ct);

            await SetStepAsync(session, "ranking_results", ct);
            var reasoning = await reasoner.RankAsync(diningIntent, candidates, ct);

            await SetStepAsync(session, "saving_results", ct);
            await db.RecommendationResults.Where(x => x.SessionId == session.Id).ExecuteDeleteAsync(ct);
            await db.RecommendationCandidates.Where(x => x.SessionId == session.Id).ExecuteDeleteAsync(ct);
            var candidateById = candidates.ToDictionary(x => x.ProviderPlaceId, StringComparer.OrdinalIgnoreCase);
            foreach (var recommendation in reasoning.Recommendations.OrderBy(x => x.Rank).Take(3))
            {
                if (!candidateById.TryGetValue(recommendation.PlaceId, out var place))
                    throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
                var candidate = new RecommendationCandidate
                {
                    SessionId = session.Id,
                    Provider = place.Provider,
                    ProviderPlaceId = place.ProviderPlaceId,
                    Name = place.Name,
                    Address = place.Address,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    Rating = place.Rating,
                    RatingCount = place.RatingCount,
                    PriceLevel = place.PriceLevel,
                    RawProviderPayloadJson = place.RawPayloadJson
                };
                db.RecommendationCandidates.Add(candidate);
                var reservation = await reservations.ResolveAsync(place, ct);
                var route = hasRealOrigin
                    ? await routes.CreateRouteUrlAsync(originalLocation.Lat!.Value, originalLocation.Lng!.Value, place.Latitude, place.Longitude, ct)
                    : null;
                var title = DisplayText.CleanUiText(place.Name, false);
                var headline = DisplayText.CleanUiText(recommendation.Label, false);
                var placeName = DisplayText.CleanUiText(place.Name, false);
                var address = DisplayText.CleanProviderAddress(place.Address);
                var vibe = DisplayText.CleanUiText(DisplayText.JoinPhraseList(recommendation.BestFor), false);
                var summary = DisplayText.CleanUiText(recommendation.Reason, true);
                var whyThisPlace = DisplayText.CleanUiText(DisplayText.JoinSentenceList(recommendation.WhyItFits), true);
                var goodToKnow = DisplayText.BuildCardGoodToKnow(reasoning.Summary, recommendation.WhyItFits, recommendation.WatchOut);
                var cautions = recommendation.WatchOut.Select(x => DisplayText.CleanUiText(x, true)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                var shareText = DisplayText.CleanUiText($"{place.Name}: {recommendation.Label}", false);
                DisplayText.ValidateRecommendationUiCopy(headline, vibe, summary, whyThisPlace, goodToKnow, cautions, shareText);
                db.RecommendationResults.Add(new RecommendationResult
                {
                    SessionId = session.Id,
                    CandidateId = candidate.Id,
                    Rank = recommendation.Rank,
                    Title = title,
                    Headline = headline,
                    Summary = summary,
                    Vibe = vibe,
                    WhyThisPlace = whyThisPlace,
                    WhatToOrderJson = JsonText.Serialize(Array.Empty<string>()),
                    GoodToKnow = goodToKnow,
                    CautionsJson = JsonText.Serialize(cautions),
                    Confidence = recommendation.Confidence.Equals("high", StringComparison.OrdinalIgnoreCase) ? 0.9 : recommendation.Confidence.Equals("medium", StringComparison.OrdinalIgnoreCase) ? 0.7 : 0.45,
                    ReservationJson = JsonText.Serialize(reservation),
                    RouteUrl = route,
                    ShareText = shareText
                });
            }

            session.Status = "completed";
            session.CurrentStep = null;
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.ErrorCode = null;
            session.ErrorMessage = null;
            await events.TrackAsync(session.UserId, new InteractionEventRequest("RecommendationViewed", "RecommendationSession", session.Id.ToString(), new Dictionary<string, object> { ["source"] = "background_job" }), ct);
            await db.SaveChangesAsync(ct);
            await notifier.CompletedAsync(session.UserId, new RecommendationStatusChanged(session.Id, session.Status, session.CurrentStep), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Recommendation job failed for session {SessionId}", job.SessionId);
            var failure = ex as ProviderFailureException;
            await MarkFailedAsync(session, failure?.Code ?? "recommendation_failed", failure?.ClientMessage ?? "We could not complete this recommendation right now.", ct);
        }
    }

    private async Task<DiningIntent> BuildDiningIntentAsync(RecommendationSession session, string[] cues, Dictionary<string, object> context, LocationInput location, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == session.UserId, ct);
        var taste = await db.TasteProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == session.UserId, ct);
        var dining = await db.DiningProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == session.UserId, ct);
        var saved = await db.SavedPlaces.AsNoTracking().Where(x => x.UserId == session.UserId).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var history = await db.RecommendationSessions.AsNoTracking()
            .Where(x => x.UserId == session.UserId && x.Id != session.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new HistoryItemDto(x.Id, x.RawText, x.LocationMode, x.LocationText, x.CreatedAt, Array.Empty<RecommendationCardDto>()))
            .ToListAsync(ct);

        return new DiningIntent(
            session.Id,
            session.RawText,
            session.Language,
            cues,
            context,
            location,
            profile == null ? null : new ProfileDto(profile.DisplayName, profile.PreferredLanguage, profile.Country, profile.Currency, profile.DistanceUnit),
            taste == null ? null : new TasteProfileDto(JsonText.Deserialize(taste.FavoriteCuisinesJson, Array.Empty<string>()), JsonText.Deserialize(taste.DislikedCuisinesJson, Array.Empty<string>()), JsonText.Deserialize(taste.FavoriteDishesJson, Array.Empty<string>()), JsonText.Deserialize(taste.DislikedIngredientsJson, Array.Empty<string>()), taste.SpiceTolerance, taste.SweetSaltyPreference, JsonText.Deserialize(taste.DrinkPreferencesJson, Array.Empty<string>()), JsonText.Deserialize(taste.DietaryRestrictionsJson, Array.Empty<string>()), JsonText.Deserialize(taste.AllergiesJson, Array.Empty<string>())),
            dining == null ? null : new DiningProfileDto(dining.UsuallyWithKids, dining.PrefersQuietPlaces, dining.PrefersOutdoor, dining.BudgetSensitivity, dining.LikesLocalExperiences, dining.LikesPremiumPlaces, dining.NeedsParking, dining.NeedsAccessibility, dining.DefaultDistanceMeters),
            saved.Select(x => new SavedPlaceDto(x.Id, x.Provider, x.ProviderPlaceId, x.RecommendationResultId, x.Name, x.Address, x.Note, x.CreatedAt)).ToList(),
            history);
    }

    private static void ApplyFamilyContextSignals(Dictionary<string, object> normalized, string[] cues, Dictionary<string, object> context, bool usuallyWithKids)
    {
        var withKids = TryReadBool(context, "withKids");
        var hasChildren = TryReadBool(context, "hasChildren");
        var cueImpliesFamily = cues.Any(x => x.Equals("family", StringComparison.OrdinalIgnoreCase) || x.Equals("family_friendly", StringComparison.OrdinalIgnoreCase));
        var family = withKids == true
            || (hasChildren != false && withKids != false && (hasChildren == true || cueImpliesFamily || usuallyWithKids || TryReadBool(normalized, "hasChildren") == true));

        normalized["hasChildren"] = family;
        if (withKids == true || (family && !context.ContainsKey("withKids")))
            normalized["withKids"] = true;
    }

    private static bool? TryReadBool(Dictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null) return null;
        if (value is bool b) return b;
        if (value is JsonElement element && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)) return element.GetBoolean();
        if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static string LocationConfidence(LocationInput location)
    {
        if (location.Lat is not null && location.Lng is not null) return "coordinates";
        if (!string.IsNullOrWhiteSpace(location.PlaceId)) return "place_id";
        if (!string.IsNullOrWhiteSpace(location.Text)) return "text_location";
        return "text_location";
    }

    private static void NormalizeLocationAssumptions(Dictionary<string, object> assumptions, string locationConfidence, bool useMockProvider)
    {
        assumptions["locationConfidence"] = useMockProvider ? "mock_resolved" : locationConfidence;
        if (!useMockProvider && assumptions.TryGetValue("radiusMeters", out var value) && value?.ToString()?.Contains("mock provider", StringComparison.OrdinalIgnoreCase) == true)
            assumptions.Remove("radiusMeters");
        if (!useMockProvider && assumptions.TryGetValue("radiusMeters", out value) && value is not int and not long and not double and not decimal)
            assumptions.Remove("radiusMeters");
    }

    private async Task MarkRunningAsync(RecommendationSession session, string step, CancellationToken ct)
    {
        session.Status = "running";
        session.CurrentStep = step;
        session.StartedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.StatusChangedAsync(session.UserId, new RecommendationStatusChanged(session.Id, session.Status, session.CurrentStep), ct);
    }

    private async Task SetStepAsync(RecommendationSession session, string step, CancellationToken ct)
    {
        session.CurrentStep = step;
        await db.SaveChangesAsync(ct);
        await notifier.StatusChangedAsync(session.UserId, new RecommendationStatusChanged(session.Id, session.Status, session.CurrentStep), ct);
    }

    private async Task MarkFailedAsync(RecommendationSession session, string errorCode, string errorMessage, CancellationToken ct)
    {
        session.Status = "failed";
        session.CurrentStep = null;
        session.FailedAt = DateTimeOffset.UtcNow;
        session.ErrorCode = errorCode;
        session.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(ct);
        await notifier.FailedAsync(session.UserId, new RecommendationFailedEvent(session.Id, session.Status, session.ErrorCode, session.ErrorMessage), ct);
    }
}

internal sealed class InMemoryRecommendationJobQueue : IRecommendationJobQueue
{
    private readonly Channel<RecommendationJob> _channel = Channel.CreateUnbounded<RecommendationJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(RecommendationJob job, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<RecommendationJob> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}

internal sealed class RecommendationProcessingWorker(
    IRecommendationJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<RecommendationProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueIncompleteSessionsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            RecommendationJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<RecommendationProcessor>();
                await processor.ProcessAsync(job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled recommendation worker error for session {SessionId}", job.SessionId);
            }
        }
    }

    private async Task RequeueIncompleteSessionsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DineCueDbContext>();
            var sessions = await db.RecommendationSessions
                .Where(x => x.Status == "queued" || x.Status == "running")
                .ToListAsync(stoppingToken);
            foreach (var session in sessions)
            {
                if (session.Status == "running")
                {
                    session.Status = "queued";
                    session.CurrentStep = null;
                }
                await queue.EnqueueAsync(new RecommendationJob(session.Id, session.UserId), stoppingToken);
            }
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not requeue incomplete recommendation sessions on startup.");
        }
    }
}

internal sealed class ProviderFailureException(string code, string clientMessage, Exception? innerException = null, string? repairFeedback = null) : Exception(clientMessage, innerException)
{
    public string Code { get; } = code;
    public string ClientMessage { get; } = clientMessage;
    public string? RepairFeedback { get; } = repairFeedback;
}

internal sealed class GooglePlacesProvider(
    HttpClient http,
    Microsoft.Extensions.Options.IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesProvider> logger) : IPlaceSearchProvider
{
    private static readonly HashSet<string> FoodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "restaurant", "cafe", "bar", "bakery", "meal_takeaway", "meal_delivery", "food"
    };

    public async Task<IReadOnlyList<PlaceCandidate>> SearchAsync(DiningIntent intent, CancellationToken ct)
    {
        var config = options.Value;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 1, 60)));
        var requestCt = timeoutCts.Token;

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(intent));
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress,places.location,places.rating,places.userRatingCount,places.priceLevel,places.types,places.primaryType,places.googleMapsUri,places.websiteUri,places.currentOpeningHours.openNow,places.businessStatus");
        request.Content = new StringContent(BuildRequestBody(intent, config), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, requestCt);
            logger.LogInformation("Google Places search completed with status {StatusCode}", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new ProviderFailureException("places_provider_failed", "We could not search places right now.");

            await using var stream = await response.Content.ReadAsStreamAsync(requestCt);
            var json = await JsonNode.ParseAsync(stream, cancellationToken: requestCt);
            var places = json?["places"]?.AsArray();
            if (places == null) return [];

            var candidates = new List<PlaceCandidate>();
            foreach (var place in places)
            {
                var candidate = NormalizePlace(place);
                if (candidate == null) continue;
                candidates.Add(candidate);
                if (candidates.Count >= Math.Clamp(config.MaxCandidates, 1, 20)) break;
            }
            return candidates;
        }
        catch (ProviderFailureException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google Places search failed.");
            throw new ProviderFailureException("places_provider_failed", "We could not search places right now.", ex);
        }
    }

    private static string BuildEndpoint(DiningIntent intent)
    {
        var broad = string.IsNullOrWhiteSpace(intent.RawText) || intent.RawText.Trim().Length < 24;
        return intent.Location.Lat is not null && intent.Location.Lng is not null && broad
            ? "https://places.googleapis.com/v1/places:searchNearby"
            : "https://places.googleapis.com/v1/places:searchText";
    }

    private static string BuildRequestBody(DiningIntent intent, GooglePlacesOptions config)
    {
        if (BuildEndpoint(intent).EndsWith("searchNearby", StringComparison.Ordinal))
        {
            return JsonText.Serialize(new
            {
                includedTypes = new[] { "restaurant", "cafe", "bar" },
                maxResultCount = Math.Clamp(config.MaxCandidates, 1, 20),
                locationRestriction = new
                {
                    circle = new
                    {
                        center = new { latitude = intent.Location.Lat, longitude = intent.Location.Lng },
                        radius = Math.Clamp(config.DefaultRadiusMeters, 100, 50000)
                    }
                },
                rankPreference = "POPULARITY",
                languageCode = intent.Language
            });
        }

        var queryParts = new[] { intent.RawText, intent.Location.Text, "restaurant cafe bar" }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var body = new Dictionary<string, object?>
        {
            ["textQuery"] = string.Join(" ", queryParts),
            ["maxResultCount"] = Math.Clamp(config.MaxCandidates, 1, 20),
            ["languageCode"] = intent.Language
        };
        if (intent.Location.Lat is not null && intent.Location.Lng is not null)
        {
            body["locationBias"] = new
            {
                circle = new
                {
                    center = new { latitude = intent.Location.Lat, longitude = intent.Location.Lng },
                    radius = Math.Clamp(config.DefaultRadiusMeters, 100, 50000)
                }
            };
        }
        return JsonText.Serialize(body);
    }

    private static PlaceCandidate? NormalizePlace(JsonNode? place)
    {
        var id = place?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id)) return null;
        var businessStatus = place?["businessStatus"]?.GetValue<string>();
        if (string.Equals(businessStatus, "CLOSED_PERMANENTLY", StringComparison.OrdinalIgnoreCase)) return null;
        var types = place?["types"]?.AsArray().Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray() ?? [];
        var primaryType = place?["primaryType"]?.GetValue<string>();
        if (!types.Any(FoodTypes.Contains) && (primaryType == null || !FoodTypes.Contains(primaryType))) return null;
        var lat = place?["location"]?["latitude"]?.GetValue<double?>();
        var lng = place?["location"]?["longitude"]?.GetValue<double?>();
        if (lat is null || lng is null) return null;

        var name = place?["displayName"]?["text"]?.GetValue<string>() ?? "Unnamed place";
        var raw = JsonText.Serialize(new
        {
            googleMapsUri = place?["googleMapsUri"]?.GetValue<string>(),
            websiteUri = place?["websiteUri"]?.GetValue<string>(),
            types,
            primaryType,
            openNow = place?["currentOpeningHours"]?["openNow"]?.GetValue<bool?>()
        });
        return new PlaceCandidate(
            "google_places",
            id,
            name,
            place?["formattedAddress"]?.GetValue<string>() ?? "",
            lat.Value,
            lng.Value,
            place?["rating"]?.GetValue<decimal?>(),
            place?["userRatingCount"]?.GetValue<int?>(),
            ParsePriceLevel(place?["priceLevel"]?.GetValue<string>()),
            raw);
    }

    private static int? ParsePriceLevel(string? value) => value switch
    {
        "PRICE_LEVEL_INEXPENSIVE" => 1,
        "PRICE_LEVEL_MODERATE" => 2,
        "PRICE_LEVEL_EXPENSIVE" => 3,
        "PRICE_LEVEL_VERY_EXPENSIVE" => 4,
        _ => null
    };
}

internal sealed class OpenAIRecommendationReasoner(
    HttpClient http,
    Microsoft.Extensions.Options.IOptions<OpenAIOptions> options,
    ILogger<OpenAIRecommendationReasoner> logger) : IRecommendationReasoner
{
    public async Task<RecommendationReasoningResult> RankAsync(DiningIntent intent, IReadOnlyList<PlaceCandidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");

        Exception? lastError = null;
        string? repairFeedback = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await CallOpenAiAsync(intent, candidates, attempt > 0, repairFeedback, ct);
                Validate(result, candidates);
                return result;
            }
            catch (ProviderFailureException ex)
            {
                lastError = ex;
                repairFeedback = ex.RepairFeedback;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        logger.LogWarning(lastError, "OpenAI recommendation reasoning failed validation.");
        throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.", lastError);
    }

    private async Task<RecommendationReasoningResult> CallOpenAiAsync(DiningIntent intent, IReadOnlyList<PlaceCandidate> candidates, bool repair, string? repairFeedback, CancellationToken ct)
    {
        var config = options.Value;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 1, 120)));
        var requestCt = timeoutCts.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(JsonText.Serialize(new
        {
            model = config.Model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "You are DineCue's dining assistant. Return only valid JSON. Pick only from candidate placeId values. Do not invent places or exact menu items. Write like a thoughtful person speaking directly to the diner: warm, natural, concise, and UI-ready. Before returning JSON, proofread every visible text field. Missing spaces between words are unacceptable. Do not concatenate Turkish words accidentally. Use natural punctuation. Never use backend, provider, product, debugging, or analysis language in user-facing fields. Avoid phrases like user request, candidate, provider data, session, request, context, criteria, Kullanıcının isteği, bu aday, Dinner için uygun, Nutzeranfrage, Kandidat, or Provider-Daten. For Turkish, write natural Turkish with correct spacing and punctuation. For English, use conversational English, not SaaS-style wording. For German, use natural conversational German, not literal English. Do not claim a place is family-friendly, quiet, reservation-capable, open, or otherwise guaranteed unless candidate metadata supports it. When certainty is limited, use soft wording: görünüyor, olabilir, kontrol etmek iyi olur, looks like, may be, worth checking, wirkt, könnte, es wäre gut zu prüfen. Supported languages: en, de, tr." },
                new { role = "user", content = BuildPrompt(intent, candidates, repair, repairFeedback) }
            }
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, requestCt);
            logger.LogInformation("OpenAI recommendation call completed with status {StatusCode}", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");

            await using var stream = await response.Content.ReadAsStreamAsync(requestCt);
            var json = await JsonNode.ParseAsync(stream, cancellationToken: requestCt);
            var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(content))
                throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
            return ParseReasoning(content);
        }
        catch (ProviderFailureException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OpenAI recommendation call failed.");
            throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.", ex);
        }
    }

    private static string BuildPrompt(DiningIntent intent, IReadOnlyList<PlaceCandidate> candidates, bool repair, string? repairFeedback)
    {
        var prompt = new
        {
            instruction = repair ? "Repair the previous invalid output. Return valid JSON exactly matching the schema. Proofread all UI-facing fields. Fix missing spaces, especially in Turkish. Do not concatenate Turkish words. Do not change selected placeIds. Do not invent new restaurants. Do not change ranks unless necessary. Keep the same structured JSON shape. Remove internal/backend wording, awkward tone, missing spaces, punctuation problems, and robotic fragments." : "Rank up to 3 candidates and write copy that can be shown directly in the UI. Before returning JSON, proofread all visible text for missing spaces and natural punctuation.",
            repairFeedback = repair ? repairFeedback : null,
            copyRules = new
            {
                summary = "Short assistant note for the whole set. Speak directly and naturally. Do not mention users, requests, sessions, providers, candidates, criteria, or data.",
                label = "Short friendly label, localized naturally. Avoid technical labels.",
                reason = "One polished direct-display sentence or two short sentences. It should sound like a dining assistant, not an analysis.",
                whyItFits = "Array of 2-4 natural short sentences or sentence-like clauses. Each item must stand alone with natural spacing and punctuation. Avoid raw fragments that would become robotic if joined.",
                bestFor = "Array of 1-3 short UI labels for vibe/use case. Do not overclaim beyond candidate metadata.",
                watchOut = "Array of short, human, useful cautions. Use careful wording for uncertain family, quietness, price, crowd, opening, or reservation claims.",
                goodToKnow = "The summary field doubles as goodToKnow in the API, so make it read like a short assistant note, not a technical explanation.",
                foodStyle = "If mentioning food style, phrase it as an expectation from the place name/types, not a factual menu claim.",
                menu = "Do not invent menu items. Leave exact food suggestions out unless real menu data is provided."
            },
            bannedUiPhrases = DisplayText.ForbiddenPhrases,
            toneExamples = new
            {
                tr = new[]
                {
                    "Akşam çocukla rahatça oturabileceğiniz, çok pahalıya kaçmayan yerleri öne çıkardım.",
                    "Çocukla akşam yemeği için sade, tanıdık ve fiyatı abartmayan bir seçenek gibi duruyor.",
                    "Yoğun saatlerde biraz kalabalık olabilir."
                },
                en = new[]
                {
                    "I picked places that look relaxed enough for dinner with kids without getting too pricey.",
                    "This looks like a safe, straightforward choice for a family dinner.",
                    "Worth checking how busy it is before you go."
                },
                de = new[]
                {
                    "Ich habe Orte ausgewählt, die für ein Abendessen mit Kindern entspannt wirken und nicht zu teuer aussehen.",
                    "Wirkt wie eine solide, unkomplizierte Wahl für ein Familienessen.",
                    "Es könnte zu Stoßzeiten voller sein, als du es dir wünschst."
                }
            },
            turkishCopyRules = intent.Language.Equals("tr", StringComparison.OrdinalIgnoreCase) ? new
            {
                style = "Natural Turkish, short and clear.",
                avoid = new[] { "missing spaces between words", "awkward literal translations", "robotic bullet-fragment chains", "overconfident claims without provider support" },
                examples = new[] { "aileyle tercih edilebilir görünüyor", "Fiyat/performans tarafında daha mantıklı bir tercih olabilir.", "Lezzet odaklı ama çocukla gitmeden önce yoğunluk durumunu kontrol etmek iyi olur.", "sakinlik garantisi yok" }
            } : null,
            schema = new
            {
                summary = "string",
                interpretedIntent = new { mealMoment = "breakfast|lunch|dinner|coffee|drinks|dessert|unknown", vibe = "string", constraints = new[] { "string" }, language = "en|de|tr" },
                recommendations = new[] { new { placeId = "string", rank = 1, label = "short localized headline", matchScore = 0, reason = "string", whyItFits = new[] { "string" }, watchOut = new[] { "string" }, bestFor = new[] { "string" }, confidence = "low|medium|high" } }
            },
            request = new { intent.RawText, intent.Language, intent.SelectedCues, intent.Context, location = intent.Location },
            profile = new { intent.Profile, intent.TasteProfile, intent.DiningProfile },
            savedPlaces = intent.SavedPlaces.Select(x => new { x.ProviderPlaceId, x.Name }),
            recentHistory = intent.RecentHistory.Select(x => new { x.RawText, x.LocationText }),
            candidates = candidates.Select(x => new { placeId = x.ProviderPlaceId, x.Name, x.Address, x.Rating, x.RatingCount, x.PriceLevel, metadata = JsonText.Deserialize(x.RawPayloadJson, new Dictionary<string, object>()) })
        };
        return JsonText.Serialize(prompt);
    }

    private static RecommendationReasoningResult ParseReasoning(string content)
    {
        var json = JsonNode.Parse(content) ?? throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
        var recommendations = json["recommendations"]?.AsArray().Select(x => new ReasonedRecommendation(
            DisplayText.CleanInline(x?["placeId"]?.GetValue<string>() ?? "", false),
            x?["rank"]?.GetValue<int>() ?? 0,
            DisplayText.CleanInline(x?["label"]?.GetValue<string>() ?? "", false),
            x?["matchScore"]?.GetValue<int>() ?? -1,
            DisplayText.CleanInline(x?["reason"]?.GetValue<string>() ?? "", true),
            ReadDisplayArray(x?["whyItFits"], true),
            ReadDisplayArray(x?["watchOut"], true),
            ReadDisplayArray(x?["bestFor"], false),
            DisplayText.CleanInline(x?["confidence"]?.GetValue<string>() ?? "", false))).ToList() ?? [];
        return new RecommendationReasoningResult(
            DisplayText.CleanInline(json["summary"]?.GetValue<string>() ?? "", true),
            JsonText.Deserialize(json["interpretedIntent"]?.ToJsonString(), new Dictionary<string, object>()),
            recommendations);
    }

    private static void Validate(RecommendationReasoningResult result, IReadOnlyList<PlaceCandidate> candidates)
    {
        var candidateIds = candidates.Select(x => x.ProviderPlaceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ranks = new HashSet<int>();
        var language = result.InterpretedIntent.TryGetValue("language", out var value) ? value?.ToString() : null;
        _ = SupportedLanguages.NormalizeRequired(language);
        if (result.Recommendations.Count is < 1 or > 3)
            throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
        DisplayText.ValidateModelCopy("summary", result.Summary, requireSentence: true);
        foreach (var rec in result.Recommendations)
        {
            if (!candidateIds.Contains(rec.PlaceId) || !ranks.Add(rec.Rank) || rec.MatchScore is < 0 or > 100)
                throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
            if (!new[] { "low", "medium", "high" }.Contains(rec.Confidence, StringComparer.OrdinalIgnoreCase))
                throw new ProviderFailureException("reasoning_provider_failed", "We could not complete this recommendation right now.");
            var candidate = candidates.First(x => x.ProviderPlaceId.Equals(rec.PlaceId, StringComparison.OrdinalIgnoreCase));
            DisplayText.ValidateModelCopy("headline", rec.Label, requireSentence: false);
            DisplayText.ValidateModelCopy("summary", rec.Reason, requireSentence: true);
            DisplayText.ValidateModelCopy("vibe", rec.BestFor, requireSentence: false);
            DisplayText.ValidateModelCopy("whyThisPlace", rec.WhyItFits, requireSentence: true);
            if (rec.WatchOut.Length > 0)
                DisplayText.ValidateModelCopy("cautions", rec.WatchOut, requireSentence: false);
            if (DisplayText.LooksLikeRawConcatenatedFragments(rec.WhyItFits))
                throw DisplayText.CopyValidationFailed("whyThisPlace", "looks like raw concatenated fragments instead of natural sentences.");
            if (DisplayText.ContainsUnsupportedClaim(candidate, new[] { rec.Label, rec.Reason }, rec.WhyItFits, rec.WatchOut, rec.BestFor))
                throw DisplayText.CopyValidationFailed("recommendations", "contains unsupported factual claims.");
        }
    }

    private static string[] ReadDisplayArray(JsonNode? node, bool ensurePunctuation) =>
        node?.AsArray()
            .Select(x => DisplayText.CleanInline(x?.GetValue<string>() ?? "", ensurePunctuation))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray() ?? [];
}

internal static class DisplayText
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RoadNoLabel = new(@"(?<=\bYolu)No:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StreetNoLabel = new(@"(?<=\bCd\.)No:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AlleyNoLabel = new(@"(?<=\bSk\.)No:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SuspiciousTurkishJoinedModifier = new(
        @"\b(?:çok|daha|biraz|gayet|oldukça)(?=[A-Za-zÇĞİÖŞÜçğıöşü]{4,})|\ben(?=(?:iyi|kötü|güzel|uygun|mantıklı|rahat|sakin|pahalı|ucuz|yakın|dengeli|kalabalık)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] SoftUncertaintyTerms =
    [
        "görünüyor",
        "olabilir",
        "kontrol etmek iyi olur",
        "kesin değil",
        "looks like",
        "may be",
        "worth checking",
        "not guaranteed",
        "wirkt",
        "könnte",
        "es wäre gut zu prüfen",
        "nicht garantiert"
    ];
    private static readonly string[] QuietClaimTerms = ["quiet", "sessiz", "sakin", "ruhig"];
    private static readonly string[] FamilyClaimTerms = ["family-friendly", "child-friendly", "kid-friendly", "aile dostu", "çocuk dostu", "familienfreundlich", "kinderfreundlich"];
    private static readonly string[] ReservationClaimTerms = ["reservation", "reserve", "rezervasyon", "reservierung"];
    private static readonly string[] OpenClaimTerms = ["open now", "currently open", "şu an açık", "açık", "gerade geöffnet", "jetzt geöffnet"];
    private static readonly string[] PhoneClaimTerms = ["phone", "call", "telefon", "arama", "anrufen"];
    private static readonly string[] ExactMenuClaimTerms = ["sipariş", "order", "bestellen"];
    public static readonly string[] ForbiddenPhrases =
    [
        "user request",
        "the user requested",
        "according to the request",
        "this candidate",
        "candidate",
        "provider data",
        "based on provider data",
        "session",
        "request",
        "context",
        "criteria",
        "matches the criteria",
        "kullanıcının isteği",
        "kullanıcının talebi",
        "bu aday",
        "dinner için uygun",
        "provider data",
        "request/context/session",
        "nutzeranfrage",
        "kandidat",
        "provider-daten"
    ];

    public static string CleanInline(string value, bool ensurePunctuation)
    {
        var cleaned = Whitespace.Replace(value.Trim(), " ");
        if (!ensurePunctuation || cleaned.Length == 0 || IsTerminalPunctuation(cleaned[^1])) return cleaned;
        return cleaned + ".";
    }

    public static string CleanUiText(string value, bool ensurePunctuation)
    {
        return CleanInline(value, ensurePunctuation);
    }

    public static string CleanProviderAddress(string value)
    {
        var cleaned = CleanInline(value, false);
        cleaned = RoadNoLabel.Replace(cleaned, " No:");
        cleaned = StreetNoLabel.Replace(cleaned, " No:");
        cleaned = AlleyNoLabel.Replace(cleaned, " No:");
        return CleanInline(cleaned, false);
    }

    public static string JoinPhraseList(IEnumerable<string> values) =>
        string.Join(", ", values.Select(x => CleanInline(x, false)).Where(x => !string.IsNullOrWhiteSpace(x)));

    public static string JoinSentenceList(IEnumerable<string> values)
    {
        var cleaned = values.Select(x => CleanInline(x, false)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (cleaned.Length == 0) return "";
        var text = cleaned.Any(x => IsTerminalPunctuation(x[^1]))
            ? string.Join(" ", cleaned.Select(x => CleanInline(x, true)))
            : string.Join("; ", cleaned);
        return CleanInline(text, true);
    }

    public static bool ContainsCopyQualityIssue(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (ForbiddenPhrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            || SuspiciousTurkishJoinedModifier.IsMatch(value));

    public static void ValidateModelCopy(string field, string value, bool requireSentence)
    {
        var cleaned = CleanInline(value, false);
        if (string.IsNullOrWhiteSpace(cleaned))
            throw CopyValidationFailed(field, "is empty.");
        if (ContainsCopyQualityIssue(cleaned))
            throw CopyValidationFailed(field, "contains internal wording or suspicious joined words.");
        if (requireSentence && cleaned.Length > 40 && !IsTerminalPunctuation(cleaned[^1]))
            throw CopyValidationFailed(field, "is missing final punctuation.");
    }

    public static void ValidateModelCopy(string field, IEnumerable<string> values, bool requireSentence)
    {
        var items = values.Select(x => CleanInline(x, false)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (items.Length == 0)
            throw CopyValidationFailed(field, "is empty.");
        foreach (var item in items)
            ValidateModelCopy(field, item, requireSentence);
    }

    public static bool LooksLikeRawConcatenatedFragments(IReadOnlyCollection<string> values)
    {
        if (values.Count < 2) return false;
        var longUnpunctuatedFragments = values.Count(x =>
        {
            var cleaned = CleanInline(x, false);
            return cleaned.Length > 28 && !IsTerminalPunctuation(cleaned[^1]);
        });
        return longUnpunctuatedFragments >= 2;
    }

    public static void ValidateRecommendationUiCopy(
        string headline,
        string vibe,
        string summary,
        string whyThisPlace,
        string goodToKnow,
        IReadOnlyCollection<string> cautions,
        string shareText)
    {
        ValidateModelCopy("headline", headline, requireSentence: false);
        ValidateModelCopy("vibe", vibe, requireSentence: false);
        ValidateModelCopy("summary", summary, requireSentence: true);
        ValidateModelCopy("whyThisPlace", whyThisPlace, requireSentence: true);
        ValidateModelCopy("goodToKnow", goodToKnow, requireSentence: true);
        if (cautions.Count > 0)
            ValidateModelCopy("cautions", cautions, requireSentence: false);
        ValidateModelCopy("shareText", shareText, requireSentence: false);
        if (NearlySame(goodToKnow, summary) || NearlySame(goodToKnow, whyThisPlace) || CleanInline(goodToKnow, false).Length < 18)
            throw CopyValidationFailed("goodToKnow", "is too short or repeats another UI field.");
    }

    public static ProviderFailureException CopyValidationFailed(string field, string reason) =>
        new("reasoning_provider_failed", "We could not complete this recommendation right now.", repairFeedback: $"{field} failed copy validation: {reason} Proofread all UI-facing strings, fix missing spaces, remove internal/backend wording, remove unsupported factual claims, keep the same selected placeIds and ranks unless absolutely necessary, keep the same JSON shape, do not invent restaurants, menu items, reservation data, or internal reasoning.");

    public static bool ContainsUnsupportedClaim(PlaceCandidate candidate, params IEnumerable<string>[] textGroups)
    {
        var text = string.Join(" ", textGroups.SelectMany(x => x).Select(x => CleanInline(x, false)));
        if (string.IsNullOrWhiteSpace(text)) return false;
        var metadata = JsonText.Deserialize(candidate.RawPayloadJson, new Dictionary<string, object>());
        var hasUncertainty = ContainsAny(text, SoftUncertaintyTerms);
        if (!hasUncertainty && ContainsAny(text, QuietClaimTerms)) return true;
        if (!hasUncertainty && ContainsAny(text, FamilyClaimTerms)) return true;
        if (ContainsAny(text, ReservationClaimTerms)) return true;
        if (ContainsAny(text, PhoneClaimTerms)) return true;
        if (ContainsAny(text, ExactMenuClaimTerms)) return true;
        if (ContainsAny(text, OpenClaimTerms) && !IsOpenNow(metadata)) return true;
        return false;
    }

    public static string BuildCardGoodToKnow(string overallSummary, IReadOnlyList<string> whyItFits, IReadOnlyList<string> cautions)
    {
        var caution = cautions.Select(x => CleanUiText(x, true)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(caution)) return caution;

        var tailored = whyItFits.Skip(1).Select(x => CleanUiText(x, true)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? whyItFits.Select(x => CleanUiText(x, true)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(tailored)) return tailored;

        return CleanUiText(overallSummary, true);
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool NearlySame(string left, string right)
    {
        var a = NormalizeForComparison(left);
        var b = NormalizeForComparison(right);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        var shorter = Math.Min(a.Length, b.Length);
        var longer = Math.Max(a.Length, b.Length);
        return shorter >= 24 && (double)shorter / longer > 0.85 && (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeForComparison(string value) =>
        new(CleanInline(value, false).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsOpenNow(Dictionary<string, object> metadata)
    {
        if (!metadata.TryGetValue("openNow", out var value) || value is null) return false;
        if (value is bool b) return b;
        if (value is JsonElement element && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)) return element.GetBoolean();
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static bool IsTerminalPunctuation(char value) => value is '.' or '!' or '?' or ';' or ':' or '…';
}

internal sealed class MockRecommendationPlaceSearchProvider(IPlacesProvider places) : IPlaceSearchProvider
{
    public async Task<IReadOnlyList<PlaceCandidate>> SearchAsync(DiningIntent intent, CancellationToken cancellationToken)
    {
        var resolved = new ResolvedLocation(
            intent.Location.Mode,
            intent.Location.Text,
            intent.Location.Lat ?? 36.8841,
            intent.Location.Lng ?? 30.7056,
            intent.Location.PlaceId,
            new Dictionary<string, object>());
        return await places.SearchAsync(intent.RawText, resolved, intent.SelectedCues, cancellationToken);
    }
}

internal sealed class MockRecommendationReasoner(IAiPlaceRanker ranker) : IRecommendationReasoner
{
    public async Task<RecommendationReasoningResult> RankAsync(DiningIntent intent, IReadOnlyList<PlaceCandidate> candidates, CancellationToken cancellationToken)
    {
        var parsed = new ParsedIntent(new Dictionary<string, object> { ["language"] = intent.Language }, new Dictionary<string, object>());
        var ranked = await ranker.RankAsync(parsed, candidates, new WeatherContext("Mock", 22, "No weather constraint."), cancellationToken);
        return new RecommendationReasoningResult(
            "Mock ranking completed.",
            new Dictionary<string, object> { ["mealMoment"] = "unknown", ["vibe"] = "mock", ["constraints"] = Array.Empty<string>(), ["language"] = intent.Language },
            ranked.Take(3).Select(x => new ReasonedRecommendation(x.Candidate.ProviderPlaceId, x.Rank, x.Headline, (int)Math.Round(x.Confidence * 100), x.Summary, [x.Why], x.Cautions, [x.Vibe], x.Confidence > 0.8 ? "high" : "medium")).ToList());
    }
}

internal sealed class RestaurantService(IPlacesProvider places, ILocationResolver locations, IReservationLinkResolver reservations, IAiRestaurantFitAnalyzer fit, IInteractionEventService events) : IRestaurantService
{
    public async Task<IReadOnlyList<RestaurantSearchResultDto>> SearchAsync(RestaurantSearchRequest request, CancellationToken ct)
    {
        RequestValidation.RestaurantSearch(request);
        var location = await locations.ResolveAsync(request.Location, ct);
        var found = await places.SearchAsync(request.Query, location, [], ct);
        var result = new List<RestaurantSearchResultDto>();
        foreach (var place in found)
        {
            var reservation = await reservations.ResolveAsync(place, ct);
            result.Add(new(place.Provider, place.ProviderPlaceId, place.Name, place.Address, place.Latitude, place.Longitude, place.Rating, place.RatingCount, place.PriceLevel, reservation));
        }
        return result;
    }
    public async Task<RestaurantDetailsDto> GetAsync(string placeId, CancellationToken ct)
    {
        var place = await places.GetByPlaceIdAsync(placeId, ct) ?? throw new ApiException("not_found", "Restaurant was not found.", 404);
        var reservation = await reservations.ResolveAsync(place, ct);
        return new(place.Provider, place.ProviderPlaceId, place.Name, place.Address, place.Latitude, place.Longitude, place.Rating, place.RatingCount, place.PriceLevel, "https://example.com/" + place.ProviderPlaceId, "https://maps.google.com/?q=" + Uri.EscapeDataString(place.Name), "+90 242 000 0000", reservation);
    }
    public async Task<RestaurantFitCheckResponse> FitCheckAsync(Guid userId, string placeId, RestaurantFitCheckRequest request, CancellationToken ct)
    {
        RequestValidation.RestaurantFit(request);
        var language = SupportedLanguages.NormalizeOrDefault(request.Language);
        request = request with { Language = language };
        var place = await places.GetByPlaceIdAsync(placeId, ct) ?? throw new ApiException("not_found", "Restaurant was not found.", 404);
        var reservation = await reservations.ResolveAsync(place, ct);
        var response = await fit.AnalyzeAsync(place, request, reservation, ct);
        await events.TrackAsync(userId, new InteractionEventRequest("RestaurantFitChecked", "Restaurant", placeId, new Dictionary<string, object> { ["fitScore"] = response.FitScore }), ct);
        return response;
    }
}

internal sealed class MenuScanService(DineCueDbContext db, IMenuOcrProvider ocr, IAiMenuInterpreter ai, IInteractionEventService events) : IMenuScanService
{
    public async Task<MenuScanResponse> CreateAsync(Guid userId, MenuScanRequest request, CancellationToken ct)
    {
        RequestValidation.MenuScan(request);
        var language = SupportedLanguages.NormalizeOrDefault(request.Language);
        var text = string.IsNullOrWhiteSpace(request.OcrText) ? await ocr.ExtractTextAsync(request.ImageUrl, request.ImageBase64, ct) : request.OcrText!;
        var interpretation = await ai.InterpretAsync(text, language, request.DiningContext, ct);
        var scan = new MenuScan { UserId = userId, RestaurantPlaceId = request.RestaurantPlaceId, ImageUrl = request.ImageUrl, OcrText = text, Language = interpretation.DetectedLanguage, DiningContextJson = JsonText.Serialize(request.DiningContext ?? new Dictionary<string, object>()), RawAiResponseJson = JsonText.Serialize(interpretation) };
        db.MenuScans.Add(scan);
        foreach (var item in interpretation.Items) db.MenuScanItems.Add(new MenuScanItem { MenuScanId = scan.Id, Name = item.Name, Description = item.Description, Category = item.Category, PriceText = item.PriceText, DetectedLanguage = interpretation.DetectedLanguage, PossibleAllergensJson = JsonText.Serialize(item.PossibleAllergens), IsKidFriendly = item.IsKidFriendly, IsVegetarian = item.IsVegetarian, IsSpicy = item.IsSpicy });
        foreach (var rec in interpretation.Recommendations) db.MenuScanRecommendations.Add(new MenuScanRecommendation { MenuScanId = scan.Id, ItemName = rec.ItemName, Reason = rec.Reason, SuitabilityScore = rec.SuitabilityScore, WarningsJson = JsonText.Serialize(rec.Warnings) });
        await events.TrackAsync(userId, new InteractionEventRequest("MenuScanCreated", "MenuScan", scan.Id.ToString(), null), ct);
        await db.SaveChangesAsync(ct);
        return ToResponse(scan.Id, interpretation);
    }
    public async Task<MenuScanResponse> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var scan = await db.MenuScans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct) ?? throw new ApiException("not_found", "Menu scan was not found.", 404);
        return ToResponse(scan.Id, JsonText.Deserialize(scan.RawAiResponseJson, Empty(scan.Id)));
    }
    public async Task<MenuScanResponse> RecommendAsync(Guid userId, Guid id, CancellationToken ct) => await GetAsync(userId, id, ct);
    private static MenuInterpretation Empty(Guid id) => new("en", "No interpreted menu was stored.", [], [], [], [], []);
    private static MenuScanResponse ToResponse(Guid id, MenuInterpretation x) => new(id, x.DetectedLanguage, x.Summary, x.Sections, x.Items, x.Recommendations, x.Warnings, x.QuestionsToAskStaff);
}

internal sealed class InteractionEventService(DineCueDbContext db) : IInteractionEventService
{
    public async Task TrackAsync(Guid userId, InteractionEventRequest request, CancellationToken ct)
    {
        RequestValidation.Interaction(request);
        await EnsureEntityOwnershipAsync(userId, request, ct);
        db.InteractionEvents.Add(new InteractionEvent { UserId = userId, EventType = request.EventType, EntityType = request.EntityType, EntityId = request.EntityId, MetadataJson = JsonText.Serialize(request.Metadata ?? new Dictionary<string, object>()) });
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureEntityOwnershipAsync(Guid userId, InteractionEventRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId)) return;
        var entityType = request.EntityType?.Trim();
        if (string.IsNullOrWhiteSpace(entityType)) throw new ApiException("validation_error", "entityType is required when entityId is supplied.", 400);

        var entityId = ParseGuidOrNull(request.EntityId);
        var owned = entityType switch
        {
            "RecommendationSession" or "History" => entityId != null &&
                (db.RecommendationSessions.Local.Any(x => x.Id == entityId.Value && x.UserId == userId) ||
                 await db.RecommendationSessions.AsNoTracking().AnyAsync(x => x.Id == entityId.Value && x.UserId == userId, ct)),
            "RecommendationResult" => entityId != null &&
                await (from result in db.RecommendationResults.AsNoTracking()
                       join session in db.RecommendationSessions.AsNoTracking() on result.SessionId equals session.Id
                       where result.Id == entityId.Value && session.UserId == userId
                       select result.Id).AnyAsync(ct),
            "MenuScan" => entityId != null &&
                (db.MenuScans.Local.Any(x => x.Id == entityId.Value && x.UserId == userId) ||
                 await db.MenuScans.AsNoTracking().AnyAsync(x => x.Id == entityId.Value && x.UserId == userId, ct)),
            "SavedPlace" => entityId != null &&
                await db.SavedPlaces.AsNoTracking().AnyAsync(x => x.Id == entityId.Value && x.UserId == userId, ct),
            "Restaurant" => true,
            _ => false
        };
        if (!owned)
            throw new ApiException("not_found", "The referenced entity was not found.", 404);
    }

    private static Guid? ParseGuidOrNull(string value) => Guid.TryParse(value, out var id) ? id : null;
}

internal sealed class MockEmailSender(IHostEnvironment environment) : IEmailSender
{
    public Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
            Console.WriteLine($"DineCue dev OTP for {email}: {code}");
        return Task.CompletedTask;
    }
}

internal sealed class MockGoogleAuthValidator(IHostEnvironment environment) : IGoogleAuthValidator
{
    public Task<GoogleUserInfo> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new ApiException("provider_unavailable", "Google login is not configured.", 503);
        var suffix = string.IsNullOrWhiteSpace(token) ? "demo" : token.Trim().Replace(".", "").Replace("-", "");
        suffix = suffix.Length > 12 ? suffix[..12] : suffix;
        return Task.FromResult(new GoogleUserInfo($"google-{suffix}", $"google.{suffix}@example.com", "DineCue Google User", "https://example.com/avatar.png"));
    }
}

internal sealed class MockSubscriptionProvider(DineCueDbContext db) : ISubscriptionProvider
{
    public Task<bool> HasActiveProAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Subscriptions.AnyAsync(x => x.UserId == userId && x.IsActive && x.PlanType == "pro" && (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow), cancellationToken);
}

internal sealed class MockLocationResolver : ILocationResolver
{
    public Task<ResolvedLocation> ResolveAsync(LocationInput? input, CancellationToken cancellationToken)
    {
        var assumptions = new Dictionary<string, object>();
        if (input is { Lat: not null, Lng: not null })
            return Task.FromResult(new ResolvedLocation(input.Mode, input.Text, input.Lat.Value, input.Lng.Value, input.PlaceId, assumptions));
        assumptions["locationConfidence"] = "mock_resolved";
        assumptions["radiusMeters"] = 1800;
        var text = input?.Text ?? "Kaleici Antalya";
        return Task.FromResult(new ResolvedLocation(input?.Mode ?? "text", text, 36.8841, 30.7056, input?.PlaceId, assumptions));
    }
}

internal sealed class MockWeatherProvider : IWeatherProvider
{
    public Task<WeatherContext> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
        Task.FromResult(new WeatherContext("Clear and warm", 27, "Outdoor seating is likely comfortable; shaded terraces are a plus."));
}

internal sealed class MockRouteProvider : IRouteProvider
{
    public Task<string> CreateRouteUrlAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken) =>
        Task.FromResult($"https://www.google.com/maps/dir/?api=1&origin={fromLat},{fromLng}&destination={toLat},{toLng}");
}

internal sealed class MockMenuOcrProvider : IMenuOcrProvider
{
    public Task<string> ExtractTextAsync(string? imageUrl, string? imageBase64, CancellationToken cancellationToken) =>
        Task.FromResult("Lentil soup, grilled sea bass, chicken skewers, tomato pasta, pistachio baklava, ayran");
}

internal sealed class MockAiIntentParser : IAiIntentParser
{
    public Task<ParsedIntent> ParseAsync(string rawText, string[] cues, Dictionary<string, object>? context, CancellationToken cancellationToken)
    {
        var lower = rawText.ToLowerInvariant();
        var inferredCues = cues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (lower.Contains("kid") || lower.Contains("family")) inferredCues.UnionWith(["family_friendly", "not_too_loud"]);
        if (lower.Contains("date")) inferredCues.Add("date_night");
        if (lower.Contains("cheap") || lower.Contains("not too expensive") || lower.Contains("value")) inferredCues.Add("good_value");
        if (lower.Contains("quiet")) inferredCues.Add("quiet");
        var normalized = new Dictionary<string, object>
        {
            ["mealType"] = lower.Contains("coffee") ? "coffee" : lower.Contains("drink") ? "drinks" : "dinner",
            ["intent"] = inferredCues.Contains("family_friendly") ? "family_dinner" : inferredCues.Contains("date_night") ? "date_night" : "casual_meal",
            ["hasChildren"] = inferredCues.Contains("family_friendly"),
            ["budget"] = inferredCues.Contains("good_value") ? "medium" : "flexible",
            ["cues"] = inferredCues.ToArray(),
            ["radiusMeters"] = 1800
        };
        if (context != null) foreach (var pair in context) normalized[pair.Key] = pair.Value;
        var assumptions = new Dictionary<string, object>
        {
            ["mealType"] = "Inferred from free-form text when not explicit.",
            ["budget"] = "Defaulted to medium/flexible from wording.",
            ["radiusMeters"] = "Default area radius used for MVP mock provider."
        };
        return Task.FromResult(new ParsedIntent(normalized, assumptions));
    }
}

internal sealed class MockPlacesProvider : IPlacesProvider
{
    private static readonly PlaceCandidate[] Seed =
    [
        new("mock", "mock-kaleici-garden", "Kaleici Garden Table", "Kilicarslan Mah. Hesapci Sok. No:12, Antalya", 36.8847, 30.7042, 4.6m, 842, 2, "{\"tags\":[\"family\",\"terrace\",\"local\"]}"),
        new("mock", "mock-harbor-meze", "Harbor Meze House", "Selcuk Mah. Iskele Cad. No:4, Antalya", 36.8862, 30.7048, 4.5m, 531, 2, "{\"tags\":[\"seafood\",\"date\",\"reservation\"]}"),
        new("mock", "mock-quiet-courtyard", "Quiet Courtyard Cafe", "Barbaros Mah. Courtyard Lane 8, Antalya", 36.8834, 30.7061, 4.4m, 219, 1, "{\"tags\":[\"coffee\",\"quiet\",\"vegetarian\"]}"),
        new("mock", "mock-ocakbasi-local", "Local Ocakbasi", "Tuzcular Mah. Local Street 22, Antalya", 36.8850, 30.7074, 4.7m, 1290, 2, "{\"tags\":[\"grill\",\"groups\",\"local\"]}"),
        new("mock", "mock-vegetarian-nook", "Vegetarian Nook", "Kale Kapisi 5, Antalya", 36.8870, 30.7069, 4.3m, 300, 2, "{\"tags\":[\"vegetarian\",\"healthy\",\"casual\"]}")
    ];

    public Task<IReadOnlyList<PlaceCandidate>> SearchAsync(string query, ResolvedLocation location, string[] cues, CancellationToken cancellationToken)
    {
        var selected = Seed.AsEnumerable();
        if (cues.Contains("date_night")) selected = selected.OrderByDescending(x => x.ProviderPlaceId.Contains("harbor"));
        else if (cues.Contains("family_friendly")) selected = selected.OrderByDescending(x => x.ProviderPlaceId.Contains("garden"));
        else if (query.Contains("coffee", StringComparison.OrdinalIgnoreCase)) selected = selected.OrderByDescending(x => x.ProviderPlaceId.Contains("courtyard"));
        return Task.FromResult<IReadOnlyList<PlaceCandidate>>(selected.Take(5).ToList());
    }

    public Task<PlaceCandidate?> GetByPlaceIdAsync(string placeId, CancellationToken cancellationToken) =>
        Task.FromResult(Seed.FirstOrDefault(x => x.ProviderPlaceId.Equals(placeId, StringComparison.OrdinalIgnoreCase)));
}

internal sealed class MockAiPlaceRanker : IAiPlaceRanker
{
    public Task<IReadOnlyList<RankedPlace>> RankAsync(ParsedIntent intent, IReadOnlyList<PlaceCandidate> candidates, WeatherContext weather, CancellationToken cancellationToken)
    {
        var list = candidates.Take(3).Select((candidate, index) =>
        {
            var family = intent.NormalizedContext.TryGetValue("hasChildren", out var hasChildren) && hasChildren is true;
            var headline = family
                ? "Easy choice for families without feeling like a generic tourist stop."
                : "A confident fit for the mood with low decision friction.";
            return new RankedPlace(
                candidate,
                index + 1,
                headline,
                $"{candidate.Name} matches the context, has dependable ratings, and works well with today's weather: {weather.DiningImpact}",
                candidate.ProviderPlaceId.Contains("courtyard") ? "quiet, relaxed, leafy" : candidate.ProviderPlaceId.Contains("harbor") ? "lively, scenic, polished" : "warm, local, comfortable",
                family ? "It balances child-friendly signals, value, and a central location." : "It lines up with the requested vibe and avoids over-filtering the choice.",
                candidate.ProviderPlaceId.Contains("meze") ? ["mixed meze", "grilled sea bass", "house lemonade"] : ["lentil soup", "chicken skewers", "seasonal salad"],
                "Book or call ahead for peak dinner hours.",
                family ? ["Confirm high chairs and non-smoking seating with staff."] : ["Menu and opening details are mock data in local development."],
                0.86 - (index * 0.04));
        }).ToList();
        return Task.FromResult<IReadOnlyList<RankedPlace>>(list);
    }
}

internal sealed class MockReservationLinkResolver : IReservationLinkResolver
{
    public Task<ReservationDto> ResolveAsync(PlaceCandidate place, CancellationToken cancellationToken)
    {
        if (place.Provider.Equals("google_places", StringComparison.OrdinalIgnoreCase))
        {
            var metadata = JsonText.Deserialize(place.RawPayloadJson, new Dictionary<string, object>());
            var googleMapsUri = metadata.TryGetValue("googleMapsUri", out var value) ? value?.ToString() : null;
            var url = !string.IsNullOrWhiteSpace(googleMapsUri)
                ? googleMapsUri
                : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(place.Name)}&query_place_id={Uri.EscapeDataString(place.ProviderPlaceId)}";
            return Task.FromResult(new ReservationDto("google_maps_only", "google_maps", url, null, 0.86));
        }
        if (place.ProviderPlaceId.Contains("harbor"))
            return Task.FromResult(new ReservationDto("available", "restaurant_website", "https://example.com/reserve/harbor-meze", "+90 242 000 0001", 0.84));
        if (place.ProviderPlaceId.Contains("garden"))
            return Task.FromResult(new ReservationDto("phone_only", "phone", null, "+90 242 000 0002", 0.72));
        return Task.FromResult(new ReservationDto("google_maps_only", "google_maps", "https://maps.google.com/?q=" + Uri.EscapeDataString(place.Name), null, 0.62));
    }
}

internal sealed class MockAiRestaurantFitAnalyzer : IAiRestaurantFitAnalyzer
{
    public Task<RestaurantFitCheckResponse> AnalyzeAsync(PlaceCandidate restaurant, RestaurantFitCheckRequest request, ReservationDto reservation, CancellationToken cancellationToken)
    {
        var text = request.RawText?.ToLowerInvariant() ?? "";
        var family = text.Contains("kid") || text.Contains("family");
        var score = restaurant.ProviderPlaceId.Contains("garden") ? 86 : family ? 76 : 82;
        return Task.FromResult(new RestaurantFitCheckResponse(
            score,
            score >= 80 ? "Good fit" : "Possible fit",
            family ? "This looks suitable for two families with kids, especially if you go early." : "This looks aligned with the requested situation, with a few details to confirm.",
            ["Menu signals are broad enough for mixed preferences.", "Location and rating history are strong for the MVP context."],
            ["It may get crowded later in the evening.", "Live availability is not connected yet."],
            family ? ["Do you have high chairs?", "Do you have a non-smoking indoor area?"] : ["Do you recommend booking?", "Which seating area is quieter?"],
            0.82,
            new Dictionary<string, object> { ["familyFriendlySignal"] = family, ["rating"] = restaurant.Rating ?? 0, ["priceLevel"] = restaurant.PriceLevel ?? 0 },
            reservation.Status == "available" ? "Reserve ahead for dinner." : "Call before going if timing matters.",
            reservation));
    }
}

internal sealed class MockAiMenuInterpreter : IAiMenuInterpreter
{
    public Task<MenuInterpretation> InterpretAsync(string text, string language, Dictionary<string, object>? diningContext, CancellationToken cancellationToken)
    {
        var items = new[]
        {
            new MenuItemDto("Lentil soup", "Comforting red lentil soup with lemon.", "Starters", "120 TRY", [], true, true, false),
            new MenuItemDto("Grilled sea bass", "Simple grilled fish with greens.", "Mains", "520 TRY", ["fish"], false, false, false),
            new MenuItemDto("Chicken skewers", "Charcoal grilled chicken with rice.", "Mains", "360 TRY", [], true, false, false),
            new MenuItemDto("Tomato pasta", "Pasta with tomato sauce and herbs.", "Kids and simple plates", "240 TRY", ["gluten"], true, true, false),
            new MenuItemDto("Pistachio baklava", "Sweet pastry with pistachio.", "Desserts", "180 TRY", ["nuts", "gluten"], true, true, false)
        };
        var recs = new[]
        {
            new MenuRecommendationDto("Lentil soup", "Good starter when you want something familiar and vegetarian.", 0.88, []),
            new MenuRecommendationDto("Chicken skewers", "Straightforward option children are likely to accept.", 0.84, []),
            new MenuRecommendationDto("Grilled sea bass", "Best fit for a lighter local dinner.", 0.8, ["Contains fish."])
        };
        var sections = new[]
        {
            new MenuSectionDto("Starters", ["Lentil soup"]),
            new MenuSectionDto("Mains", ["Grilled sea bass", "Chicken skewers", "Tomato pasta"]),
            new MenuSectionDto("Desserts", ["Pistachio baklava"])
        };
        return Task.FromResult(new MenuInterpretation(
            language,
            "Mock menu interpretation found simple family-friendly dishes, vegetarian options, and common allergen signals.",
            sections,
            items,
            recs,
            ["Menu information may be incomplete or inaccurate. If you have allergies or dietary restrictions, confirm ingredients with the restaurant staff before ordering."],
            ["Can you confirm allergens for this dish?", "Is this cooked separately from seafood or nuts?", "Can spice level be adjusted?"]));
    }
}
