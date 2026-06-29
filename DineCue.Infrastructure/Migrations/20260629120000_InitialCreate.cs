using System;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260629120000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS users (
            "Id" uuid PRIMARY KEY,
            "Email" varchar(320) NOT NULL,
            "DisplayName" text NULL,
            "AvatarUrl" text NULL,
            "PreferredLanguage" text NOT NULL,
            "Country" text NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL,
            "DeletedAt" timestamptz NULL,
            "FirstLoginAt" timestamptz NULL,
            "LastLoginAt" timestamptz NULL,
            "LoginCount" integer NOT NULL,
            "OnboardingCompletedAt" timestamptz NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_Email" ON users ("Email");

        CREATE TABLE IF NOT EXISTS user_identities (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "Provider" varchar(64) NOT NULL,
            "ProviderUserId" text NOT NULL,
            "Email" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_identities_Provider_ProviderUserId" ON user_identities ("Provider", "ProviderUserId");

        CREATE TABLE IF NOT EXISTS email_otps (
            "Id" uuid PRIMARY KEY,
            "Email" varchar(320) NOT NULL,
            "CodeHash" text NOT NULL,
            "ExpiresAt" timestamptz NOT NULL,
            "ConsumedAt" timestamptz NULL,
            "AttemptCount" integer NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_email_otps_Email_ConsumedAt" ON email_otps ("Email", "ConsumedAt");

        CREATE TABLE IF NOT EXISTS refresh_tokens (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "TokenHash" text NOT NULL,
            "ExpiresAt" timestamptz NOT NULL,
            "RevokedAt" timestamptz NULL,
            "CreatedAt" timestamptz NOT NULL,
            "ReplacedByTokenId" uuid NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");

        CREATE TABLE IF NOT EXISTS user_profiles (
            "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
            "DisplayName" text NULL,
            "PreferredLanguage" text NOT NULL,
            "Country" text NULL,
            "Currency" text NOT NULL,
            "DistanceUnit" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS taste_profiles (
            "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
            "FavoriteCuisinesJson" text NOT NULL,
            "DislikedCuisinesJson" text NOT NULL,
            "FavoriteDishesJson" text NOT NULL,
            "DislikedIngredientsJson" text NOT NULL,
            "SpiceTolerance" integer NOT NULL,
            "SweetSaltyPreference" text NOT NULL,
            "DrinkPreferencesJson" text NOT NULL,
            "DietaryRestrictionsJson" text NOT NULL,
            "AllergiesJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS dining_profiles (
            "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
            "UsuallyWithKids" boolean NOT NULL,
            "PrefersQuietPlaces" boolean NOT NULL,
            "PrefersOutdoor" boolean NOT NULL,
            "BudgetSensitivity" integer NOT NULL,
            "LikesLocalExperiences" boolean NOT NULL,
            "LikesPremiumPlaces" boolean NOT NULL,
            "NeedsParking" boolean NOT NULL,
            "NeedsAccessibility" boolean NOT NULL,
            "DefaultDistanceMeters" integer NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS onboarding_states (
            "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
            "CompletedStepsJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS daily_usages (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "UsageDate" date NOT NULL,
            "RecommendationSessionCount" integer NOT NULL,
            "MenuScanCount" integer NOT NULL,
            "FitCheckCount" integer NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_daily_usages_UserId_UsageDate" ON daily_usages ("UserId", "UsageDate");

        CREATE TABLE IF NOT EXISTS subscriptions (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "PlanType" text NOT NULL,
            "IsActive" boolean NOT NULL,
            "Provider" text NULL,
            "ExternalSubscriptionId" text NULL,
            "ExpiresAt" timestamptz NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recommendation_sessions (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "RawText" text NOT NULL,
            "Language" text NOT NULL,
            "LocationMode" text NOT NULL,
            "LocationText" text NULL,
            "Latitude" double precision NULL,
            "Longitude" double precision NULL,
            "PlaceId" text NULL,
            "SelectedCuesJson" text NOT NULL,
            "InputContextJson" text NOT NULL,
            "NormalizedContextJson" text NOT NULL,
            "AssumptionsJson" text NOT NULL,
            "WeatherContextJson" text NULL,
            "Status" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recommendation_candidates (
            "Id" uuid PRIMARY KEY,
            "SessionId" uuid NOT NULL REFERENCES recommendation_sessions("Id") ON DELETE CASCADE,
            "Provider" text NOT NULL,
            "ProviderPlaceId" text NOT NULL,
            "Name" text NOT NULL,
            "Address" text NOT NULL,
            "Latitude" double precision NOT NULL,
            "Longitude" double precision NOT NULL,
            "Rating" numeric NULL,
            "RatingCount" integer NULL,
            "PriceLevel" integer NULL,
            "RawProviderPayloadJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recommendation_results (
            "Id" uuid PRIMARY KEY,
            "SessionId" uuid NOT NULL REFERENCES recommendation_sessions("Id") ON DELETE CASCADE,
            "CandidateId" uuid NOT NULL REFERENCES recommendation_candidates("Id") ON DELETE CASCADE,
            "Rank" integer NOT NULL,
            "Title" text NOT NULL,
            "Headline" text NOT NULL,
            "Summary" text NOT NULL,
            "Vibe" text NOT NULL,
            "WhyThisPlace" text NOT NULL,
            "WhatToOrderJson" text NOT NULL,
            "GoodToKnow" text NOT NULL,
            "CautionsJson" text NOT NULL,
            "Confidence" double precision NOT NULL,
            "ReservationJson" text NOT NULL,
            "RouteUrl" text NULL,
            "ShareText" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS restaurant_snapshots (
            "Id" uuid PRIMARY KEY,
            "Provider" text NOT NULL,
            "ProviderPlaceId" text NOT NULL,
            "Name" text NOT NULL,
            "Address" text NOT NULL,
            "Latitude" double precision NOT NULL,
            "Longitude" double precision NOT NULL,
            "Rating" numeric NULL,
            "RatingCount" integer NULL,
            "PriceLevel" integer NULL,
            "OpeningHoursJson" text NOT NULL,
            "PhotosJson" text NOT NULL,
            "ReviewsJson" text NOT NULL,
            "WebsiteUrl" text NULL,
            "GoogleMapsUri" text NULL,
            "PhoneNumber" text NULL,
            "RawProviderPayloadJson" text NOT NULL,
            "FetchedAt" timestamptz NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS restaurant_insights (
            "Id" uuid PRIMARY KEY,
            "ProviderPlaceId" text NOT NULL,
            "Language" text NOT NULL,
            "FamilyFriendlyScore" integer NOT NULL,
            "DateNightScore" integer NOT NULL,
            "QuietScore" integer NOT NULL,
            "GroupFriendlyScore" integer NOT NULL,
            "VegetarianScore" integer NOT NULL,
            "KidMenuSignal" boolean NOT NULL,
            "AlcoholSignal" boolean NOT NULL,
            "ParkingSignal" boolean NOT NULL,
            "Summary" text NOT NULL,
            "ProsJson" text NOT NULL,
            "ConsJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS restaurant_reservation_links (
            "Id" uuid PRIMARY KEY,
            "Provider" text NOT NULL,
            "ProviderPlaceId" text NOT NULL,
            "ReservationProvider" text NOT NULL,
            "ReservationUrl" text NULL,
            "PhoneNumber" text NULL,
            "Source" text NOT NULL,
            "Confidence" double precision NOT NULL,
            "LastCheckedAt" timestamptz NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS menu_scans (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "RestaurantPlaceId" text NULL,
            "ImageUrl" text NULL,
            "OcrText" text NULL,
            "Language" text NOT NULL,
            "DiningContextJson" text NOT NULL,
            "Status" text NOT NULL,
            "RawAiResponseJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS menu_scan_items (
            "Id" uuid PRIMARY KEY,
            "MenuScanId" uuid NOT NULL REFERENCES menu_scans("Id") ON DELETE CASCADE,
            "Name" text NOT NULL,
            "Description" text NOT NULL,
            "Category" text NOT NULL,
            "PriceText" text NULL,
            "DetectedLanguage" text NOT NULL,
            "PossibleAllergensJson" text NOT NULL,
            "IsKidFriendly" boolean NOT NULL,
            "IsVegetarian" boolean NOT NULL,
            "IsSpicy" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS menu_scan_recommendations (
            "Id" uuid PRIMARY KEY,
            "MenuScanId" uuid NOT NULL REFERENCES menu_scans("Id") ON DELETE CASCADE,
            "ItemName" text NOT NULL,
            "Reason" text NOT NULL,
            "SuitabilityScore" double precision NOT NULL,
            "WarningsJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS saved_places (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "Provider" text NOT NULL,
            "ProviderPlaceId" text NOT NULL,
            "RecommendationResultId" uuid NULL,
            "Name" text NOT NULL,
            "Address" text NOT NULL,
            "Note" text NULL,
            "CreatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_saved_places_UserId_ProviderPlaceId" ON saved_places ("UserId", "ProviderPlaceId");

        CREATE TABLE IF NOT EXISTS recommendation_feedback (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "RecommendationResultId" uuid NOT NULL REFERENCES recommendation_results("Id") ON DELETE CASCADE,
            "Went" boolean NULL,
            "Liked" boolean NULL,
            "Rating" integer NULL,
            "WouldGoAgain" boolean NULL,
            "Note" text NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_recommendation_feedback_UserId_RecommendationResultId" ON recommendation_feedback ("UserId", "RecommendationResultId");

        CREATE TABLE IF NOT EXISTS interaction_events (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "EventType" text NOT NULL,
            "EntityType" text NULL,
            "EntityId" text NULL,
            "MetadataJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_interaction_events_UserId_CreatedAt" ON interaction_events ("UserId", "CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DROP TABLE IF EXISTS interaction_events;
        DROP TABLE IF EXISTS recommendation_feedback;
        DROP TABLE IF EXISTS saved_places;
        DROP TABLE IF EXISTS menu_scan_recommendations;
        DROP TABLE IF EXISTS menu_scan_items;
        DROP TABLE IF EXISTS menu_scans;
        DROP TABLE IF EXISTS restaurant_reservation_links;
        DROP TABLE IF EXISTS restaurant_insights;
        DROP TABLE IF EXISTS restaurant_snapshots;
        DROP TABLE IF EXISTS recommendation_results;
        DROP TABLE IF EXISTS recommendation_candidates;
        DROP TABLE IF EXISTS recommendation_sessions;
        DROP TABLE IF EXISTS subscriptions;
        DROP TABLE IF EXISTS daily_usages;
        DROP TABLE IF EXISTS onboarding_states;
        DROP TABLE IF EXISTS dining_profiles;
        DROP TABLE IF EXISTS taste_profiles;
        DROP TABLE IF EXISTS user_profiles;
        DROP TABLE IF EXISTS refresh_tokens;
        DROP TABLE IF EXISTS email_otps;
        DROP TABLE IF EXISTS user_identities;
        DROP TABLE IF EXISTS users;
        """);
    }
}
