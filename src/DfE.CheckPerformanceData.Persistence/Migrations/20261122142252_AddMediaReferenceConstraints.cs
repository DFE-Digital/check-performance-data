using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaReferenceConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Usages are derived from content and can be rebuilt, so a usage whose page is not
            // the page of its page version is removed before the database enforces the match.
            migrationBuilder.Sql("""
                DELETE FROM media_usages u
                WHERE u.page_node_version_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "PageNodeVersions" v
                                  WHERE v."Id" = u.page_node_version_id AND v."PageNodeId" = u.page_node_id);
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_media_usages_page_node_versions",
                table: "media_usages");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PageNodeVersions_Id_PageNodeId",
                table: "PageNodeVersions",
                columns: new[] { "Id", "PageNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_media_usages_page_node_version_id_page_node_id",
                table: "media_usages",
                columns: new[] { "page_node_version_id", "page_node_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_media_usages_page_node_versions",
                table: "media_usages",
                columns: new[] { "page_node_version_id", "page_node_id" },
                principalTable: "PageNodeVersions",
                principalColumns: new[] { "Id", "PageNodeId" },
                onDelete: ReferentialAction.Cascade);

            // An image's current version must be one of its stored versions. The image and its
            // first version are added in one save, and a replace adds a version and moves the
            // current version in one save, so the check waits for the end of the transaction
            // (DEFERRABLE INITIALLY DEFERRED). The model cannot describe a deferred key that
            // forms a cycle with the versions' key to the image, so it is added here in SQL.
            // An image whose current version is missing first takes its highest stored version.
            migrationBuilder.Sql("""
                UPDATE media_assets a
                SET current_version = (SELECT max(v.version) FROM media_asset_versions v WHERE v.asset_id = a.id)
                WHERE NOT EXISTS (SELECT 1 FROM media_asset_versions v WHERE v.asset_id = a.id AND v.version = a.current_version)
                  AND EXISTS (SELECT 1 FROM media_asset_versions v WHERE v.asset_id = a.id);

                ALTER TABLE media_assets
                    ADD CONSTRAINT fk_media_assets_media_asset_versions_current_version
                    FOREIGN KEY (id, current_version) REFERENCES media_asset_versions (asset_id, version)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE media_assets DROP CONSTRAINT IF EXISTS fk_media_assets_media_asset_versions_current_version;");

            migrationBuilder.DropForeignKey(
                name: "fk_media_usages_page_node_versions",
                table: "media_usages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PageNodeVersions_Id_PageNodeId",
                table: "PageNodeVersions");

            migrationBuilder.DropIndex(
                name: "IX_media_usages_page_node_version_id_page_node_id",
                table: "media_usages");

            migrationBuilder.AddForeignKey(
                name: "fk_media_usages_page_node_versions",
                table: "media_usages",
                column: "page_node_version_id",
                principalTable: "PageNodeVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
