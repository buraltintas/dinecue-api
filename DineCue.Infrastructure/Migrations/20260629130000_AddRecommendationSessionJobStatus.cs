using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260629130000_AddRecommendationSessionJobStatus")]
public partial class AddRecommendationSessionJobStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE recommendation_sessions
            ADD COLUMN IF NOT EXISTS "CurrentStep" text NULL,
            ADD COLUMN IF NOT EXISTS "StartedAt" timestamptz NULL,
            ADD COLUMN IF NOT EXISTS "CompletedAt" timestamptz NULL,
            ADD COLUMN IF NOT EXISTS "FailedAt" timestamptz NULL,
            ADD COLUMN IF NOT EXISTS "ErrorCode" text NULL,
            ADD COLUMN IF NOT EXISTS "ErrorMessage" text NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE recommendation_sessions
            DROP COLUMN IF EXISTS "CurrentStep",
            DROP COLUMN IF EXISTS "StartedAt",
            DROP COLUMN IF EXISTS "CompletedAt",
            DROP COLUMN IF EXISTS "FailedAt",
            DROP COLUMN IF EXISTS "ErrorCode",
            DROP COLUMN IF EXISTS "ErrorMessage";
        """);
    }
}
