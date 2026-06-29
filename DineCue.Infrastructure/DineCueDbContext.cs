using DineCue.Domain;
using Microsoft.EntityFrameworkCore;

namespace DineCue.Infrastructure;

public sealed class DineCueDbContext(DbContextOptions<DineCueDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<TasteProfile> TasteProfiles => Set<TasteProfile>();
    public DbSet<DiningProfile> DiningProfiles => Set<DiningProfile>();
    public DbSet<OnboardingState> OnboardingStates => Set<OnboardingState>();
    public DbSet<DailyUsage> DailyUsages => Set<DailyUsage>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<RecommendationSession> RecommendationSessions => Set<RecommendationSession>();
    public DbSet<RecommendationCandidate> RecommendationCandidates => Set<RecommendationCandidate>();
    public DbSet<RecommendationResult> RecommendationResults => Set<RecommendationResult>();
    public DbSet<RestaurantSnapshot> RestaurantSnapshots => Set<RestaurantSnapshot>();
    public DbSet<RestaurantInsight> RestaurantInsights => Set<RestaurantInsight>();
    public DbSet<RestaurantReservationLink> RestaurantReservationLinks => Set<RestaurantReservationLink>();
    public DbSet<MenuScan> MenuScans => Set<MenuScan>();
    public DbSet<MenuScanItem> MenuScanItems => Set<MenuScanItem>();
    public DbSet<MenuScanRecommendation> MenuScanRecommendations => Set<MenuScanRecommendation>();
    public DbSet<SavedPlace> SavedPlaces => Set<SavedPlace>();
    public DbSet<RecommendationFeedback> RecommendationFeedback => Set<RecommendationFeedback>();
    public DbSet<InteractionEvent> InteractionEvents => Set<InteractionEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasMany(x => x.Identities).WithOne().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<UserProfile>(x => x.UserId);
            e.HasOne(x => x.TasteProfile).WithOne().HasForeignKey<TasteProfile>(x => x.UserId);
            e.HasOne(x => x.DiningProfile).WithOne().HasForeignKey<DiningProfile>(x => x.UserId);
            e.HasOne<OnboardingState>().WithOne().HasForeignKey<OnboardingState>(x => x.UserId);
        });

        b.Entity<UserIdentity>(e =>
        {
            e.ToTable("user_identities");
            e.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(64);
        });

        b.Entity<EmailOtp>(e =>
        {
            e.ToTable("email_otps");
            e.HasIndex(x => new { x.Email, x.ConsumedAt });
            e.Property(x => x.Email).HasMaxLength(320);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<UserProfile>(e =>
        {
            e.ToTable("user_profiles");
            e.HasKey(x => x.UserId);
        });

        b.Entity<TasteProfile>(e =>
        {
            e.ToTable("taste_profiles");
            e.HasKey(x => x.UserId);
        });

        b.Entity<DiningProfile>(e =>
        {
            e.ToTable("dining_profiles");
            e.HasKey(x => x.UserId);
        });

        b.Entity<OnboardingState>(e =>
        {
            e.ToTable("onboarding_states");
            e.HasKey(x => x.UserId);
        });

        b.Entity<DailyUsage>(e =>
        {
            e.ToTable("daily_usages");
            e.HasIndex(x => new { x.UserId, x.UsageDate }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<RecommendationSession>(e =>
        {
            e.ToTable("recommendation_sessions");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.Candidates).WithOne().HasForeignKey(x => x.SessionId);
            e.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.SessionId);
        });

        b.Entity<RecommendationCandidate>(e =>
        {
            e.ToTable("recommendation_candidates");
            e.HasIndex(x => new { x.SessionId, x.ProviderPlaceId });
        });

        b.Entity<RecommendationResult>(e =>
        {
            e.ToTable("recommendation_results");
            e.HasIndex(x => new { x.SessionId, x.Rank });
        });

        b.Entity<RestaurantSnapshot>(e =>
        {
            e.ToTable("restaurant_snapshots");
            e.HasIndex(x => new { x.Provider, x.ProviderPlaceId }).IsUnique();
        });

        b.Entity<RestaurantInsight>(e =>
        {
            e.ToTable("restaurant_insights");
            e.HasIndex(x => new { x.ProviderPlaceId, x.Language });
        });

        b.Entity<RestaurantReservationLink>(e =>
        {
            e.ToTable("restaurant_reservation_links");
            e.HasIndex(x => new { x.Provider, x.ProviderPlaceId });
        });

        b.Entity<MenuScan>(e =>
        {
            e.ToTable("menu_scans");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.MenuScanId);
            e.HasMany(x => x.Recommendations).WithOne().HasForeignKey(x => x.MenuScanId);
        });

        b.Entity<MenuScanItem>().ToTable("menu_scan_items");
        b.Entity<MenuScanRecommendation>().ToTable("menu_scan_recommendations");

        b.Entity<SavedPlace>(e =>
        {
            e.ToTable("saved_places");
            e.HasIndex(x => new { x.UserId, x.ProviderPlaceId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<RecommendationFeedback>(e =>
        {
            e.ToTable("recommendation_feedback");
            e.HasIndex(x => new { x.UserId, x.RecommendationResultId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<RecommendationResult>().WithMany().HasForeignKey(x => x.RecommendationResultId);
        });

        b.Entity<InteractionEvent>(e =>
        {
            e.ToTable("interaction_events");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
