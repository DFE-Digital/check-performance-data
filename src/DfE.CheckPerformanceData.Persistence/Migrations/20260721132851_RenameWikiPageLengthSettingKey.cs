using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    // Data-only migration: rename any existing "Wiki:PageLength" row in the Settings
    // table to "CMS:PageLength" so the value an admin previously configured survives
    // the constant rename. No schema changes.
    //
    // The Delete-before-Update guards against the (unlikely but possible) case where
    // both rows exist post-hoc: a "CMS:PageLength" row wins and the wiki-prefixed
    // orphan is discarded. Both branches are idempotent and safe on a fresh DB where
    // neither row exists.
    public partial class RenameWikiPageLengthSettingKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "Settings"
                    WHERE "Key" = 'Wiki:PageLength'
                      AND EXISTS (SELECT 1 FROM "Settings" WHERE "Key" = 'CMS:PageLength');

                UPDATE "Settings"
                    SET "Key" = 'CMS:PageLength'
                    WHERE "Key" = 'Wiki:PageLength';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "Settings"
                    WHERE "Key" = 'CMS:PageLength'
                      AND EXISTS (SELECT 1 FROM "Settings" WHERE "Key" = 'Wiki:PageLength');

                UPDATE "Settings"
                    SET "Key" = 'Wiki:PageLength'
                    WHERE "Key" = 'CMS:PageLength';
                """);
        }
    }
}
