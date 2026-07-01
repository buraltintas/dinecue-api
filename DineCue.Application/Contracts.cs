using System.Security.Claims;

namespace DineCue.Application;

public sealed record ApiError(string Code, string Message, object? Details = null);
public sealed record ApiErrorEnvelope(ApiError Error);

public sealed record UserDto(Guid Id, string Email, string? DisplayName, string? AvatarUrl, string PreferredLanguage, string? Country);
public sealed record LoginResponse(string AccessToken, string RefreshToken, UserDto User, bool IsNewUser, bool OnboardingCompleted);
public sealed record EmailStartRequest(string Email, string? PreferredLanguage = null);
public sealed record EmailStartResponse(string Message, string? DevOtp);
public sealed record EmailVerifyRequest(string Email, string Code, string? PreferredLanguage = null);
public sealed record GoogleLoginRequest(string Token, string? PreferredLanguage = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);

public sealed record ProfileDto(string? DisplayName, string PreferredLanguage, string? Country, string Currency, string DistanceUnit);
public sealed record TasteProfileDto(
    string[] FavoriteCuisines,
    string[] DislikedCuisines,
    string[] FavoriteDishes,
    string[] DislikedIngredients,
    int SpiceTolerance,
    string SweetSaltyPreference,
    string[] DrinkPreferences,
    string[] DietaryRestrictions,
    string[] Allergies);
public sealed record DiningProfileDto(
    bool UsuallyWithKids,
    bool PrefersQuietPlaces,
    bool PrefersOutdoor,
    int BudgetSensitivity,
    bool LikesLocalExperiences,
    bool LikesPremiumPlaces,
    bool NeedsParking,
    bool NeedsAccessibility,
    int DefaultDistanceMeters);
public sealed record OnboardingStatusResponse(bool Completed, DateTimeOffset? CompletedAt, string[] CompletedSteps);
public sealed record CompleteOnboardingRequest(string[] CompletedSteps);

public sealed record LocationInput(string Mode, string? Text, double? Lat, double? Lng, string? PlaceId);
public sealed record RecommendationSessionRequest(string RawText, LocationInput? Location, string[]? SelectedCues, string? Language, Dictionary<string, object>? Context);
public sealed record RefineRecommendationRequest(string RawText, string[]? SelectedCues, Dictionary<string, object>? Context);
public sealed record RecommendationSessionResponse(
    Guid SessionId,
    Dictionary<string, object> NormalizedContext,
    Dictionary<string, object> Assumptions,
    IReadOnlyList<RecommendationCardDto> Recommendations);
public sealed record RecommendationSessionAcceptedResponse(Guid SessionId, string Status, string StatusUrl);
public sealed record RecommendationSessionDetailResponse(
    Guid SessionId,
    string Status,
    string? CurrentStep,
    string RawText,
    string Language,
    string LocationMode,
    string? LocationText,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? ErrorCode,
    string? ErrorMessage,
    Dictionary<string, object> NormalizedContext,
    Dictionary<string, object> Assumptions,
    IReadOnlyList<RecommendationCardDto> Recommendations);
public sealed record RecommendationStatusChanged(Guid SessionId, string Status, string? CurrentStep);
public sealed record RecommendationFailedEvent(Guid SessionId, string Status, string ErrorCode, string ErrorMessage);
public sealed record RecommendationJob(Guid SessionId, Guid UserId);
public sealed record RecommendationCardDto(
    Guid Id,
    int Rank,
    string Title,
    string Headline,
    string PlaceName,
    string Address,
    string Vibe,
    string Summary,
    string WhyThisPlace,
    string[] WhatToOrder,
    string GoodToKnow,
    string[] Cautions,
    double Confidence,
    ReservationDto Reservation,
    string? RouteUrl,
    string ShareText);
public sealed record ReservationDto(string Status, string Provider, string? Url, string? PhoneNumber, double Confidence);

public sealed record RestaurantSearchRequest(string Query, LocationInput? Location, string? Language);
public sealed record RestaurantSearchResultDto(string Provider, string ProviderPlaceId, string Name, string Address, double Latitude, double Longitude, decimal? Rating, int? RatingCount, int? PriceLevel, ReservationDto Reservation);
public sealed record RestaurantDetailsDto(string Provider, string ProviderPlaceId, string Name, string Address, double Latitude, double Longitude, decimal? Rating, int? RatingCount, int? PriceLevel, string? WebsiteUrl, string? GoogleMapsUri, string? PhoneNumber, ReservationDto Reservation);
public sealed record RestaurantFitCheckRequest(string? RawText, string? Language, Dictionary<string, object>? Context);
public sealed record RestaurantFitCheckResponse(int FitScore, string Verdict, string Summary, string[] Pros, string[] Cons, string[] QuestionsToAsk, double Confidence, Dictionary<string, object> RelevantSignals, string ReservationAdvice, ReservationDto Reservation);

public sealed record MenuScanRequest(string? ImageUrl, string? ImageBase64, string? OcrText, string? RestaurantPlaceId, string? Language, Dictionary<string, object>? DiningContext);
public sealed record MenuScanResponse(Guid MenuScanId, string DetectedLanguage, string Summary, MenuSectionDto[] Sections, MenuItemDto[] Items, MenuRecommendationDto[] RecommendedItems, string[] Warnings, string[] QuestionsToAskStaff);
public sealed record MenuSectionDto(string Name, string[] ItemNames);
public sealed record MenuItemDto(string Name, string Description, string Category, string? PriceText, string[] PossibleAllergens, bool IsKidFriendly, bool IsVegetarian, bool IsSpicy);
public sealed record MenuRecommendationDto(string ItemName, string Reason, double SuitabilityScore, string[] Warnings);

public sealed record SavedPlaceDto(Guid Id, string Provider, string ProviderPlaceId, Guid? RecommendationResultId, string Name, string Address, string? Note, DateTimeOffset CreatedAt);
public sealed record FeedbackRequest(bool? Went, bool? Liked, int? Rating, bool? WouldGoAgain, string? Note);
public sealed record FeedbackDto(Guid Id, Guid RecommendationResultId, bool? Went, bool? Liked, int? Rating, bool? WouldGoAgain, string? Note);
public sealed record ShareTextResponse(string ShareText);
public sealed record HistoryItemDto(Guid SessionId, string RawText, string LocationMode, string? LocationText, DateTimeOffset CreatedAt, IReadOnlyList<RecommendationCardDto> Results);
public sealed record InteractionEventRequest(string EventType, string? EntityType, string? EntityId, Dictionary<string, object>? Metadata);

public sealed record GoogleUserInfo(string ProviderUserId, string Email, string? DisplayName, string? AvatarUrl);
public sealed record PlaceCandidate(string Provider, string ProviderPlaceId, string Name, string Address, double Latitude, double Longitude, decimal? Rating, int? RatingCount, int? PriceLevel, string RawPayloadJson);
public sealed record ResolvedLocation(string Mode, string? Text, double Latitude, double Longitude, string? PlaceId, Dictionary<string, object> Assumptions);
public sealed record WeatherContext(string Summary, int TemperatureC, string DiningImpact);
public sealed record ParsedIntent(Dictionary<string, object> NormalizedContext, Dictionary<string, object> Assumptions);
public sealed record RankedPlace(PlaceCandidate Candidate, int Rank, string Headline, string Summary, string Vibe, string Why, string[] WhatToOrder, string GoodToKnow, string[] Cautions, double Confidence);
public sealed record MenuInterpretation(string DetectedLanguage, string Summary, MenuSectionDto[] Sections, MenuItemDto[] Items, MenuRecommendationDto[] Recommendations, string[] Warnings, string[] QuestionsToAskStaff);
public sealed record DiningIntent(
    Guid SessionId,
    string RawText,
    string Language,
    string[] SelectedCues,
    Dictionary<string, object> Context,
    LocationInput Location,
    ProfileDto? Profile,
    TasteProfileDto? TasteProfile,
    DiningProfileDto? DiningProfile,
    IReadOnlyList<SavedPlaceDto> SavedPlaces,
    IReadOnlyList<HistoryItemDto> RecentHistory);
public sealed record ReasonedRecommendation(string PlaceId, int Rank, string Label, int MatchScore, string Reason, string[] WhyItFits, string[] WatchOut, string[] BestFor, string Confidence);
public sealed record RecommendationReasoningResult(string Summary, Dictionary<string, object> InterpretedIntent, IReadOnlyList<ReasonedRecommendation> Recommendations);

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string Locale,
    Dictionary<string, string>? Metadata = null);
public sealed record EmailSendResult(bool Succeeded, string? ProviderMessageId = null, string? ErrorCode = null);
public sealed record QuotaStateResponse(
    string Plan,
    int MonthlyLimit,
    int UsedThisPeriod,
    int RemainingThisPeriod,
    string PeriodKey,
    DateTimeOffset PeriodEndsAt,
    bool ProAvailable,
    string ProStatus);
public sealed record EmailTemplateModel(
    string Locale,
    string? DisplayName = null,
    string? Code = null,
    string? LinkUrl = null,
    int? ExpiresInMinutes = null,
    string? SenderEmail = null,
    string? Message = null);
public sealed record MonthlyRecapPlaceEmailModel(string PlaceName, string Headline, string WhyThisPlace);
public sealed record MonthlyRecapEmailModel(
    string Locale,
    string MonthLabel,
    int DiningDecisionCount,
    IReadOnlyList<MonthlyRecapPlaceEmailModel> Places,
    string? HistoryUrl = null,
    string? DisplayName = null);
public sealed record RenderedEmailTemplate(string Subject, string HtmlBody, string TextBody);
public interface IEmailTemplateRenderer
{
    RenderedEmailTemplate RenderWelcome(EmailTemplateModel model);
    RenderedEmailTemplate RenderEmailVerification(EmailTemplateModel model);
    RenderedEmailTemplate RenderMonthlyRecap(MonthlyRecapEmailModel model);
    RenderedEmailTemplate RenderContactFeedbackNotification(EmailTemplateModel model);
}
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
    Task<EmailSendResult> SendOtpAsync(string email, string code, string? locale, CancellationToken cancellationToken);
}
public interface IEmailNotificationService
{
    Task SendWelcomeAsync(UserDto user, CancellationToken cancellationToken);
}
public interface IMonthlyRecapEmailService
{
    Task<int> SendMonthlyRecapsAsync(string periodKey, CancellationToken cancellationToken);
}
public interface IGoogleAuthValidator { Task<GoogleUserInfo> ValidateAsync(string token, CancellationToken cancellationToken); }
public interface ITokenService
{
    string CreateAccessToken(Guid userId, string email, string preferredLanguage);
    string CreateRefreshToken();
    string HashToken(string token);
    string HashOtp(string email, string code);
}
public interface IPlacesProvider
{
    Task<IReadOnlyList<PlaceCandidate>> SearchAsync(string query, ResolvedLocation location, string[] cues, CancellationToken cancellationToken);
    Task<PlaceCandidate?> GetByPlaceIdAsync(string placeId, CancellationToken cancellationToken);
}
public interface ILocationResolver { Task<ResolvedLocation> ResolveAsync(LocationInput? input, CancellationToken cancellationToken); }
public interface IWeatherProvider { Task<WeatherContext> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken); }
public interface IRouteProvider { Task<string> CreateRouteUrlAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken); }
public interface IMenuOcrProvider { Task<string> ExtractTextAsync(string? imageUrl, string? imageBase64, CancellationToken cancellationToken); }
public interface IAiIntentParser { Task<ParsedIntent> ParseAsync(string rawText, string[] cues, Dictionary<string, object>? context, CancellationToken cancellationToken); }
public interface IAiPlaceRanker { Task<IReadOnlyList<RankedPlace>> RankAsync(ParsedIntent intent, IReadOnlyList<PlaceCandidate> candidates, WeatherContext weather, CancellationToken cancellationToken); }
public interface IAiRestaurantFitAnalyzer { Task<RestaurantFitCheckResponse> AnalyzeAsync(PlaceCandidate restaurant, RestaurantFitCheckRequest request, ReservationDto reservation, CancellationToken cancellationToken); }
public interface IAiMenuInterpreter { Task<MenuInterpretation> InterpretAsync(string text, string language, Dictionary<string, object>? diningContext, CancellationToken cancellationToken); }
public interface IQuotaService
{
    Task<QuotaStateResponse> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<QuotaStateResponse> ReserveRecommendationAsync(Guid userId, CancellationToken cancellationToken);
    Task<QuotaStateResponse> ReserveMenuScanAsync(Guid userId, CancellationToken cancellationToken);
    Task<QuotaStateResponse> ReserveRestaurantFitCheckAsync(Guid userId, CancellationToken cancellationToken);
    Task ReleaseRecommendationAsync(Guid userId, CancellationToken cancellationToken);
    Task ReleaseMenuScanAsync(Guid userId, CancellationToken cancellationToken);
    Task ReleaseRestaurantFitCheckAsync(Guid userId, CancellationToken cancellationToken);
}
public interface IReservationLinkResolver { Task<ReservationDto> ResolveAsync(PlaceCandidate place, CancellationToken cancellationToken); }
public interface ISubscriptionProvider { Task<bool> HasActiveProAsync(Guid userId, CancellationToken cancellationToken); }
public interface IPlaceSearchProvider { Task<IReadOnlyList<PlaceCandidate>> SearchAsync(DiningIntent intent, CancellationToken cancellationToken); }
public interface IRecommendationReasoner { Task<RecommendationReasoningResult> RankAsync(DiningIntent intent, IReadOnlyList<PlaceCandidate> candidates, CancellationToken cancellationToken); }

public interface IAuthService
{
    Task<EmailStartResponse> StartEmailAsync(EmailStartRequest request, CancellationToken cancellationToken);
    Task<LoginResponse> VerifyEmailAsync(EmailVerifyRequest request, CancellationToken cancellationToken);
    Task<LoginResponse> GoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken);
    Task<LoginResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken cancellationToken);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken cancellationToken);
    Task DeleteMeAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IProfileService
{
    Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileDto request, CancellationToken cancellationToken);
    Task<OnboardingStatusResponse> GetOnboardingAsync(Guid userId, CancellationToken cancellationToken);
    Task<OnboardingStatusResponse> CompleteOnboardingAsync(Guid userId, CompleteOnboardingRequest request, CancellationToken cancellationToken);
    Task<TasteProfileDto> GetTasteAsync(Guid userId, CancellationToken cancellationToken);
    Task<TasteProfileDto> UpdateTasteAsync(Guid userId, TasteProfileDto request, CancellationToken cancellationToken);
    Task<DiningProfileDto> GetDiningAsync(Guid userId, CancellationToken cancellationToken);
    Task<DiningProfileDto> UpdateDiningAsync(Guid userId, DiningProfileDto request, CancellationToken cancellationToken);
}

public interface IRecommendationService
{
    Task<RecommendationSessionAcceptedResponse> CreateAsync(Guid userId, RecommendationSessionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<HistoryItemDto>> ListHistoryAsync(Guid userId, CancellationToken cancellationToken);
    Task<RecommendationSessionDetailResponse> GetAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<RecommendationSessionAcceptedResponse> RefineAsync(Guid userId, Guid sessionId, RefineRecommendationRequest request, CancellationToken cancellationToken);
    Task<SavedPlaceDto> SaveAsync(Guid userId, Guid recommendationResultId, CancellationToken cancellationToken);
    Task UnsSaveAsync(Guid userId, Guid recommendationResultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPlaceDto>> GetSavedAsync(Guid userId, CancellationToken cancellationToken);
    Task<FeedbackDto> UpsertFeedbackAsync(Guid userId, Guid recommendationResultId, FeedbackRequest request, CancellationToken cancellationToken);
    Task<ShareTextResponse> ShareTextAsync(Guid userId, Guid recommendationResultId, CancellationToken cancellationToken);
}

public interface IRecommendationJobQueue
{
    ValueTask EnqueueAsync(RecommendationJob job, CancellationToken cancellationToken);
    ValueTask<RecommendationJob> DequeueAsync(CancellationToken cancellationToken);
}

public interface IRecommendationStatusNotifier
{
    Task StatusChangedAsync(Guid userId, RecommendationStatusChanged status, CancellationToken cancellationToken);
    Task CompletedAsync(Guid userId, RecommendationStatusChanged status, CancellationToken cancellationToken);
    Task FailedAsync(Guid userId, RecommendationFailedEvent failure, CancellationToken cancellationToken);
}

public interface IRestaurantService
{
    Task<IReadOnlyList<RestaurantSearchResultDto>> SearchAsync(RestaurantSearchRequest request, CancellationToken cancellationToken);
    Task<RestaurantDetailsDto> GetAsync(string placeId, CancellationToken cancellationToken);
    Task<RestaurantFitCheckResponse> FitCheckAsync(Guid userId, string placeId, RestaurantFitCheckRequest request, CancellationToken cancellationToken);
}

public interface IMenuScanService
{
    Task<MenuScanResponse> CreateAsync(Guid userId, MenuScanRequest request, CancellationToken cancellationToken);
    Task<MenuScanResponse> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);
    Task<MenuScanResponse> RecommendAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}

public interface IInteractionEventService { Task TrackAsync(Guid userId, InteractionEventRequest request, CancellationToken cancellationToken); }

public sealed class ApiException(string code, string message, int statusCode = 400, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public object? Details { get; } = details;
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var userId) ? userId : throw new ApiException("unauthorized", "Authentication is required.", 401);
    }
}
