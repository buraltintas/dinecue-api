using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260630123000_AddUserIdentityLastUsedAt")]
public partial class AddUserIdentityLastUsedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE user_identities
            ADD COLUMN IF NOT EXISTS "LastUsedAt" timestamptz NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE user_identities
            DROP COLUMN IF EXISTS "LastUsedAt";
        """);
    }
}
