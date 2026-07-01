using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260630120000_AddIdentityUsageLedgers")]
public partial class AddIdentityUsageLedgers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS identity_usage_ledgers (
            "Id" uuid PRIMARY KEY,
            "KeyType" varchar(64) NOT NULL,
            "KeyHash" varchar(128) NOT NULL,
            "PeriodStart" date NOT NULL,
            "RecommendationSessionCount" integer NOT NULL,
            "MenuScanCount" integer NOT NULL,
            "FitCheckCount" integer NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_identity_usage_ledgers_KeyType_KeyHash_PeriodStart"
            ON identity_usage_ledgers ("KeyType", "KeyHash", "PeriodStart");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DROP TABLE IF EXISTS identity_usage_ledgers;
        """);
    }
}
