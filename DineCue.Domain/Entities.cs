namespace DineCue.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class User : Entity
{
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public string? Country { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? FirstLoginAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public List<UserIdentity> Identities { get; set; } = [];
    public UserProfile? Profile { get; set; }
    public TasteProfile? TasteProfile { get; set; }
    public DiningProfile? DiningProfile { get; set; }
}

public sealed class UserIdentity : Entity
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderUserId { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmailOtp : Entity
{
    public string Email { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReplacedByTokenId { get; set; }
}

public sealed class UserProfile
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public string? Country { get; set; }
    public string Currency { get; set; } = "USD";
    public string DistanceUnit { get; set; } = "km";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TasteProfile
{
    public Guid UserId { get; set; }
    public string FavoriteCuisinesJson { get; set; } = "[]";
    public string DislikedCuisinesJson { get; set; } = "[]";
    public string FavoriteDishesJson { get; set; } = "[]";
    public string DislikedIngredientsJson { get; set; } = "[]";
    public int SpiceTolerance { get; set; } = 2;
    public string SweetSaltyPreference { get; set; } = "balanced";
    public string DrinkPreferencesJson { get; set; } = "[]";
    public string DietaryRestrictionsJson { get; set; } = "[]";
    public string AllergiesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DiningProfile
{
    public Guid UserId { get; set; }
    public bool UsuallyWithKids { get; set; }
    public bool PrefersQuietPlaces { get; set; }
    public bool PrefersOutdoor { get; set; }
    public int BudgetSensitivity { get; set; } = 2;
    public bool LikesLocalExperiences { get; set; } = true;
    public bool LikesPremiumPlaces { get; set; }
    public bool NeedsParking { get; set; }
    public bool NeedsAccessibility { get; set; }
    public int DefaultDistanceMeters { get; set; } = 1800;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OnboardingState
{
    public Guid UserId { get; set; }
    public string CompletedStepsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DailyUsage : Entity
{
    public Guid UserId { get; set; }
    public DateOnly UsageDate { get; set; }
    public int RecommendationSessionCount { get; set; }
    public int MenuScanCount { get; set; }
    public int FitCheckCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Subscription : Entity
{
    public Guid UserId { get; set; }
    public string PlanType { get; set; } = "free";
    public bool IsActive { get; set; }
    public string? Provider { get; set; }
    public string? ExternalSubscriptionId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecommendationSession : Entity
{
    public Guid UserId { get; set; }
    public string RawText { get; set; } = "";
    public string Language { get; set; } = "en";
    public string LocationMode { get; set; } = "current";
    public string? LocationText { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? PlaceId { get; set; }
    public string SelectedCuesJson { get; set; } = "[]";
    public string InputContextJson { get; set; } = "{}";
    public string NormalizedContextJson { get; set; } = "{}";
    public string AssumptionsJson { get; set; } = "{}";
    public string? WeatherContextJson { get; set; }
    public string Status { get; set; } = "queued";
    public string? CurrentStep { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<RecommendationCandidate> Candidates { get; set; } = [];
    public List<RecommendationResult> Results { get; set; } = [];
}

public sealed class RecommendationCandidate : Entity
{
    public Guid SessionId { get; set; }
    public string Provider { get; set; } = "mock";
    public string ProviderPlaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public decimal? Rating { get; set; }
    public int? RatingCount { get; set; }
    public int? PriceLevel { get; set; }
    public string RawProviderPayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecommendationResult : Entity
{
    public Guid SessionId { get; set; }
    public Guid CandidateId { get; set; }
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Vibe { get; set; } = "";
    public string WhyThisPlace { get; set; } = "";
    public string WhatToOrderJson { get; set; } = "[]";
    public string GoodToKnow { get; set; } = "";
    public string CautionsJson { get; set; } = "[]";
    public double Confidence { get; set; }
    public string ReservationJson { get; set; } = "{}";
    public string? RouteUrl { get; set; }
    public string ShareText { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantSnapshot : Entity
{
    public string Provider { get; set; } = "mock";
    public string ProviderPlaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public decimal? Rating { get; set; }
    public int? RatingCount { get; set; }
    public int? PriceLevel { get; set; }
    public string OpeningHoursJson { get; set; } = "{}";
    public string PhotosJson { get; set; } = "[]";
    public string ReviewsJson { get; set; } = "[]";
    public string? WebsiteUrl { get; set; }
    public string? GoogleMapsUri { get; set; }
    public string? PhoneNumber { get; set; }
    public string RawProviderPayloadJson { get; set; } = "{}";
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantInsight : Entity
{
    public string ProviderPlaceId { get; set; } = "";
    public string Language { get; set; } = "en";
    public int FamilyFriendlyScore { get; set; }
    public int DateNightScore { get; set; }
    public int QuietScore { get; set; }
    public int GroupFriendlyScore { get; set; }
    public int VegetarianScore { get; set; }
    public bool KidMenuSignal { get; set; }
    public bool AlcoholSignal { get; set; }
    public bool ParkingSignal { get; set; }
    public string Summary { get; set; } = "";
    public string ProsJson { get; set; } = "[]";
    public string ConsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantReservationLink : Entity
{
    public string Provider { get; set; } = "mock";
    public string ProviderPlaceId { get; set; } = "";
    public string ReservationProvider { get; set; } = "unknown";
    public string? ReservationUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string Source { get; set; } = "mock";
    public double Confidence { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MenuScan : Entity
{
    public Guid UserId { get; set; }
    public string? RestaurantPlaceId { get; set; }
    public string? ImageUrl { get; set; }
    public string? OcrText { get; set; }
    public string Language { get; set; } = "en";
    public string DiningContextJson { get; set; } = "{}";
    public string Status { get; set; } = "completed";
    public string RawAiResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MenuScanItem> Items { get; set; } = [];
    public List<MenuScanRecommendation> Recommendations { get; set; } = [];
}

public sealed class MenuScanItem : Entity
{
    public Guid MenuScanId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string? PriceText { get; set; }
    public string DetectedLanguage { get; set; } = "en";
    public string PossibleAllergensJson { get; set; } = "[]";
    public bool IsKidFriendly { get; set; }
    public bool IsVegetarian { get; set; }
    public bool IsSpicy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MenuScanRecommendation : Entity
{
    public Guid MenuScanId { get; set; }
    public string ItemName { get; set; } = "";
    public string Reason { get; set; } = "";
    public double SuitabilityScore { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SavedPlace : Entity
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "mock";
    public string ProviderPlaceId { get; set; } = "";
    public Guid? RecommendationResultId { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecommendationFeedback : Entity
{
    public Guid UserId { get; set; }
    public Guid RecommendationResultId { get; set; }
    public bool? Went { get; set; }
    public bool? Liked { get; set; }
    public int? Rating { get; set; }
    public bool? WouldGoAgain { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InteractionEvent : Entity
{
    public Guid UserId { get; set; }
    public string EventType { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
