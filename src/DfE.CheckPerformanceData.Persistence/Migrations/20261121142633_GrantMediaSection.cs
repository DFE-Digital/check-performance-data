using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantMediaSection : Migration
    {
        // The role names and section key, as DefaultAdminAccessSeeder writes them. A migration
        // keeps its own copy: it must not change when the code changes later.
        private const string AdminRole = "cypmd_admin";
        private const string EditorRole = "cypmd_content_access_user";
        private const string MediaSection = "media";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A database made before the media library has role grants but no "media" grant, and
            // the seeder gives the editor its defaults only on an empty table. So this gives
            // "media" to the editor and admin roles, once: EF runs a migration one time only. An
            // admin who removes the grant later keeps that change.
            //
            // A new database has no grants yet. Nothing is added there, so the seeder still finds
            // the table empty and writes the full defaults.
            foreach (var role in new[] { AdminRole, EditorRole })
            {
                migrationBuilder.Sql($"""
                    INSERT INTO "AdminSectionAccesses" ("RoleName", "SectionKey", "CreatedAt", "CreatedBy")
                    SELECT '{role}', '{MediaSection}', now(), 'system'
                    WHERE EXISTS (SELECT 1 FROM "AdminSectionAccesses")
                      AND NOT EXISTS (
                          SELECT 1 FROM "AdminSectionAccesses"
                          WHERE "RoleName" = '{role}' AND "SectionKey" = '{MediaSection}');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo. A grant cannot be told apart from one that an admin ticked by hand,
            // so a rollback leaves the grants as they are.
        }
    }
}
