using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineCue.Infrastructure.Migrations;

[DbContext(typeof(DineCueDbContext))]
[Migration("20260807120000_AddMenuScanJobAndImages")]
public partial class AddMenuScanJobAndImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        -- Menu scans became asynchronous (queued -> running -> completed/failed), mirroring recommendation
        -- sessions, and now carry the moment-specific context the diner supplied alongside their profile.
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "CurrentStep" text NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "ErrorCode" text NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "ErrorMessage" text NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "StartedAt" timestamptz NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "CompletedAt" timestamptz NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "FailedAt" timestamptz NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "RawTextInput" text NULL;
        ALTER TABLE menu_scans ADD COLUMN IF NOT EXISTS "SelectedCuesJson" text NOT NULL DEFAULT '[]';

        CREATE INDEX IF NOT EXISTS "IX_menu_scans_UserId_Status"
            ON menu_scans ("UserId", "Status");

        -- Uploaded menu photos live in a private bucket; only the object name is stored here. Rows start
        -- unattached ("MenuScanId" NULL) so uploading costs no credit and can be retried.
        CREATE TABLE IF NOT EXISTS menu_scan_images (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "MenuScanId" uuid NULL REFERENCES menu_scans("Id") ON DELETE CASCADE,
            "ObjectName" text NOT NULL,
            "ContentType" text NOT NULL,
            "SizeBytes" bigint NOT NULL,
            "Position" integer NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_menu_scan_images_ObjectName"
            ON menu_scan_images ("ObjectName");
        CREATE INDEX IF NOT EXISTS "IX_menu_scan_images_UserId_MenuScanId"
            ON menu_scan_images ("UserId", "MenuScanId");

        -- The model has always declared this unique index but the initial migration never created it, and the
        -- snapshot cache upserts against it. Duplicates from before are collapsed to the freshest row first.
        DELETE FROM restaurant_snapshots s
        USING restaurant_snapshots newer
        WHERE s."Provider" = newer."Provider"
          AND s."ProviderPlaceId" = newer."ProviderPlaceId"
          AND (s."FetchedAt", s."Id") < (newer."FetchedAt", newer."Id");

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_restaurant_snapshots_Provider_ProviderPlaceId"
            ON restaurant_snapshots ("Provider", "ProviderPlaceId");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DROP INDEX IF EXISTS "IX_restaurant_snapshots_Provider_ProviderPlaceId";
        DROP TABLE IF EXISTS menu_scan_images;
        DROP INDEX IF EXISTS "IX_menu_scans_UserId_Status";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "SelectedCuesJson";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "RawTextInput";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "FailedAt";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "CompletedAt";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "StartedAt";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "ErrorMessage";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "ErrorCode";
        ALTER TABLE menu_scans DROP COLUMN IF EXISTS "CurrentStep";
        """);
    }
}
