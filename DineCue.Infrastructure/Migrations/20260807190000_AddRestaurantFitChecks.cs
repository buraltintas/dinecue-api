using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260807190000_AddRestaurantFitChecks")]
public partial class AddRestaurantFitChecks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        -- A fit check the diner ran, stored in full and owned by them. restaurant_insights is a place-level
        -- aggregate with no owner, so before this a diner spent a monthly credit on an answer that was gone
        -- the moment they navigated away.
        CREATE TABLE IF NOT EXISTS restaurant_fit_checks (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "Provider" text NOT NULL,
            "ProviderPlaceId" text NOT NULL,
            "PlaceName" text NOT NULL,
            "Address" text NOT NULL,
            "Language" text NOT NULL,
            "RawText" text NULL,
            "ContextJson" text NOT NULL,
            "VisitAt" timestamptz NULL,
            "FitScore" integer NOT NULL,
            "Verdict" text NOT NULL,
            "Summary" text NOT NULL,
            "ProsJson" text NOT NULL,
            "ConsJson" text NOT NULL,
            "QuestionsJson" text NOT NULL,
            "Confidence" double precision NOT NULL,
            "RelevantSignalsJson" text NOT NULL,
            "ReservationAdvice" text NOT NULL,
            "ReservationJson" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        -- The history feed reads a diner's checks newest first.
        CREATE INDEX IF NOT EXISTS "IX_restaurant_fit_checks_UserId_CreatedAt"
            ON restaurant_fit_checks ("UserId", "CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DROP TABLE IF EXISTS restaurant_fit_checks;
        """);
    }
}
