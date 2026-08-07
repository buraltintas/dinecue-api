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

public sealed record ProfileQuotaDto(
    string Plan,
    int MonthlyLimit,
    int UsedThisPeriod,
    int RemainingThisPeriod,
    string PeriodKey,
    DateTimeOffset PeriodEndsAt,
    bool ProAvailable,
    string ProStatus);
public sealed record ProfileDto(
    string? DisplayName,
    string PreferredLanguage,
    string? Country,
    string Currency,
    string DistanceUnit,
    ProfileQuotaDto? Quota = null);
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
    string ProviderPlaceId,
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

/// <summary>
/// Restaurant lookup by name. <paramref name="Cues"/> currently understands "nearby", which ranks by
/// distance instead of rating so a diner can ask about the place in front of them.
/// </summary>
public sealed record RestaurantSearchRequest(string Query, LocationInput? Location, string? Language, string[]? Cues = null);
public sealed record RestaurantSearchResultDto(string Provider, string ProviderPlaceId, string Name, string Address, double Latitude, double Longitude, decimal? Rating, int? RatingCount, int? PriceLevel, ReservationDto Reservation);
public sealed record RestaurantDetailsDto(string Provider, string ProviderPlaceId, string Name, string Address, double Latitude, double Longitude, decimal? Rating, int? RatingCount, int? PriceLevel, string? WebsiteUrl, string? GoogleMapsUri, string? PhoneNumber, ReservationDto Reservation);
public sealed record RestaurantFitCheckRequest(string? RawText, string? Language, Dictionary<string, object>? Context);

/// <summary>
/// <paramref name="Id"/> and the place fields let a diner return to a check later. They are appended, so
/// the existing field names and types are untouched.
/// </summary>
public sealed record RestaurantFitCheckResponse(
    int FitScore,
    string Verdict,
    string Summary,
    string[] Pros,
    string[] Cons,
    string[] QuestionsToAsk,
    double Confidence,
    Dictionary<string, object> RelevantSignals,
    string ReservationAdvice,
    ReservationDto Reservation,
    Guid Id = default,
    string ProviderPlaceId = "",
    string PlaceName = "",
    string Address = "",
    string? RawText = null,
    DateTimeOffset? CreatedAt = null);

public sealed record MenuScanListItemDto(
    Guid MenuScanId,
    string Status,
    string Language,
    string? RawText,
    string? RestaurantPlaceId,
    string Summary,
    int ImageCount,
    int ItemCount,
    string[] SuggestedItems,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// One entry in the diner's history, whichever flow produced it. A monthly credit buys exactly one of
/// these, so the feed is the honest record of what they spent it on.
/// </summary>
public sealed record ActivityItemDto(
    string Kind,
    Guid Id,
    string Status,
    string Title,
    string? Subtitle,
    string[] Highlights,
    int? Score,
    int ImageCount,
    string? ErrorCode,
    DateTimeOffset CreatedAt);

public static class ActivityKinds
{
    public const string Recommendation = "recommendation";
    public const string MenuScan = "menu_scan";
    public const string FitCheck = "fit_check";
}

/// <summary>
/// A menu scan request. Images are uploaded first through POST /uploads/menu-images (which costs no credits)
/// and referenced here by object name. The context fields mirror the find form's vocabulary, and all of them
/// are optional because the diner's stored taste and dining profiles already carry the durable context.
/// </summary>
public sealed record MenuScanRequest(
    string? ImageUrl,
    string? ImageBase64,
    string? OcrText,
    string? RestaurantPlaceId,
    string? Language,
    Dictionary<string, object>? DiningContext,
    string[]? ImageObjectNames = null,
    string? RawText = null,
    string[]? SelectedCues = null,
    Dictionary<string, object>? Context = null);

public sealed record MenuScanAcceptedResponse(Guid MenuScanId, string Status, string StatusUrl);
public sealed record MenuImageUploadResponse(string ObjectName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
public sealed record MenuScanResponse(
    Guid MenuScanId,
    string DetectedLanguage,
    string Summary,
    MenuSectionDto[] Sections,
    MenuItemDto[] Items,
    MenuRecommendationDto[] RecommendedItems,
    string[] Warnings,
    string[] QuestionsToAskStaff,
    string Status = "completed",
    string? CurrentStep = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? CompletedAt = null,
    int ImageCount = 0,
    string? RestaurantPlaceId = null,
    /// <summary>What the diner typed and selected for this scan. Part of the record, not just our answer.</summary>
    string? RawText = null,
    string[]? SelectedCues = null);
public sealed record MenuScanJob(Guid MenuScanId, Guid UserId);

/// <summary>Everything the menu interpreter needs to read a menu and rank it for one specific diner.</summary>
public sealed record MenuInterpretationRequest(
    string? Text,
    IReadOnlyList<string> ImageDataUrls,
    string Language,
    string? RawText,
    string[] SelectedCues,
    Dictionary<string, object> Context,
    TasteProfileDto? TasteProfile,
    DiningProfileDto? DiningProfile,
    string? RestaurantMetadataJson);

/// <summary>
/// The interpreter transcribes and structures in one pass, so the transcript comes back alongside the
/// interpretation and can be reused later without paying for image reading again.
/// <paramref name="Readable"/> is false when the images are not a legible menu.
/// </summary>
public sealed record MenuInterpretationResult(string OcrText, MenuInterpretation Interpretation, bool Readable);
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
/// <summary>
/// A location after resolution. Latitude/Longitude are nullable on purpose: when free text cannot be
/// geocoded we must not substitute placeholder coordinates, because that silently searches the wrong city.
/// </summary>
public sealed record ResolvedLocation(string Mode, string? Text, double? Latitude, double? Longitude, string? PlaceId, Dictionary<string, object> Assumptions);
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
    string? Message = null,
    /// <summary>
    /// Where a new diner sets their taste and dining preferences. The welcome email is the one moment we
    /// can reasonably ask for them, and every AI flow reads them afterwards.
    /// </summary>
    string? PreferencesUrl = null);
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
public interface IWeatherProvider { Task<WeatherContext> GetCurrentAsync(double? latitude, double? longitude, CancellationToken cancellationToken); }
public interface IRouteProvider
{
    Task<string> CreateRouteUrlAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken);

    /// <summary>
    /// Directions link when we do not know where the diner is starting from. Google fills in the origin,
    /// so text and map-pin requests still get a usable route action instead of nothing.
    /// </summary>
    Task<string> CreateDestinationUrlAsync(string destinationName, string? destinationPlaceId, double toLat, double toLng, CancellationToken cancellationToken);
}
/// <summary>
/// Blob storage for user-uploaded images. The bucket stays private: images are served back through the API
/// after an ownership check, so no signed URLs or extra credentials are needed.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string objectName, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Returns null when the object does not exist.</summary>
    Task<Stream?> OpenReadAsync(string objectName, CancellationToken cancellationToken);
    Task DeleteAsync(string objectName, CancellationToken cancellationToken);
}
public interface IAiIntentParser { Task<ParsedIntent> ParseAsync(string rawText, string[] cues, Dictionary<string, object>? context, CancellationToken cancellationToken); }
public interface IAiPlaceRanker { Task<IReadOnlyList<RankedPlace>> RankAsync(ParsedIntent intent, IReadOnlyList<PlaceCandidate> candidates, WeatherContext weather, CancellationToken cancellationToken); }
/// <summary>
/// Everything the fit analyzer weighs: what the provider reports about the place, the diner's durable
/// profile, and the moment they are asking about. <paramref name="VisitAt"/> comes from context["visitAt"]
/// and is what makes opening hours a usable signal rather than trivia.
/// </summary>
public sealed record RestaurantFitContext(
    PlaceCandidate Restaurant,
    RestaurantFitCheckRequest Request,
    ReservationDto Reservation,
    TasteProfileDto? TasteProfile,
    DiningProfileDto? DiningProfile,
    DateTimeOffset? VisitAt);

public interface IAiRestaurantFitAnalyzer { Task<RestaurantFitCheckResponse> AnalyzeAsync(RestaurantFitContext context, CancellationToken cancellationToken); }
public interface IAiMenuInterpreter { Task<MenuInterpretationResult> InterpretAsync(MenuInterpretationRequest request, CancellationToken cancellationToken); }
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

    /// <summary>Returns a fit check the diner ran earlier, so a spent credit stays reviewable.</summary>
    Task<RestaurantFitCheckResponse> GetFitCheckAsync(Guid userId, Guid fitCheckId, CancellationToken cancellationToken);
}

/// <summary>
/// The diner's own record of every credit-spending action, merged chronologically across the three flows.
/// </summary>
public interface IActivityService
{
    /// <summary><paramref name="kind"/> is null for the merged feed, or one of <see cref="ActivityKinds"/> to group.</summary>
    Task<IReadOnlyList<ActivityItemDto>> ListAsync(Guid userId, string? kind, int limit, CancellationToken cancellationToken);
}

public interface IMenuScanService
{
    Task<MenuScanAcceptedResponse> CreateAsync(Guid userId, MenuScanRequest request, CancellationToken cancellationToken);
    Task<MenuScanResponse> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>The diner's past scans. Without this a completed scan was unreachable once they navigated away.</summary>
    Task<IReadOnlyList<MenuScanListItemDto>> ListAsync(Guid userId, int limit, CancellationToken cancellationToken);

    /// <summary>Re-ranks an existing scan against the diner's current profile. Costs no credits: no image is read again.</summary>
    Task<MenuScanResponse> RecommendAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Stores one uploaded image and returns its object name for a later scan. Costs no credits.</summary>
    Task<MenuImageUploadResponse> UploadImageAsync(Guid userId, Stream content, string? declaredContentType, string? fileName, CancellationToken cancellationToken);

    /// <summary>Streams a scan image back to its owner. Returns null when the scan or index does not exist.</summary>
    Task<(Stream Content, string ContentType)?> OpenImageAsync(Guid userId, Guid menuScanId, int index, CancellationToken cancellationToken);
}

public interface IMenuScanJobQueue
{
    ValueTask EnqueueAsync(MenuScanJob job, CancellationToken cancellationToken);
    ValueTask<MenuScanJob> DequeueAsync(CancellationToken cancellationToken);
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
