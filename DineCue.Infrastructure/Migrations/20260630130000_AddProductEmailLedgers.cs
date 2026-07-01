using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260630130000_AddProductEmailLedgers")]
public partial class AddProductEmailLedgers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS email_delivery_ledgers (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "EmailType" varchar(64) NOT NULL,
            "PeriodKey" varchar(16) NOT NULL,
            "Status" varchar(32) NOT NULL,
            "SentAt" timestamptz NULL,
            "ProviderMessageId" text NULL,
            "ErrorCode" text NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_email_delivery_ledgers_UserId_EmailType_PeriodKey"
            ON email_delivery_ledgers ("UserId", "EmailType", "PeriodKey");

        CREATE TABLE IF NOT EXISTS notification_preferences (
            "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
            "MonthlyRecapEnabled" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DROP TABLE IF EXISTS notification_preferences;
        DROP TABLE IF EXISTS email_delivery_ledgers;
        """);
    }
}
