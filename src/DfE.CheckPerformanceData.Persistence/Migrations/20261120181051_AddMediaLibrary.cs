using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    default_alt_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    caption = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    credit = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.id);
                    table.CheckConstraint("ck_media_assets_current_version", "current_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "media_asset_versions",
                columns: table => new
                {
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    blob_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    is_animated = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_asset_versions", x => new { x.asset_id, x.version });
                    table.CheckConstraint("ck_media_asset_versions_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_media_asset_versions_media_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_version = table.Column<int>(type: "integer", nullable: false),
                    page_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_node_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    widget_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_usages", x => x.id);
                    table.CheckConstraint("ck_media_usages_location", "location IN ('widget', 'richtext')");
                    table.ForeignKey(
                        name: "fk_media_usages_media_asset_versions",
                        columns: x => new { x.asset_id, x.asset_version },
                        principalTable: "media_asset_versions",
                        principalColumns: new[] { "asset_id", "version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_media_usages_page_node_versions",
                        column: x => x.page_node_version_id,
                        principalTable: "PageNodeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_usages_page_nodes",
                        column: x => x.page_node_id,
                        principalTable: "PageNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_asset_versions_sha256",
                table: "media_asset_versions",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ux_media_asset_versions_blob_key",
                table: "media_asset_versions",
                column: "blob_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_updated_at_utc",
                table: "media_assets",
                column: "updated_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_media_usages_asset_id_asset_version",
                table: "media_usages",
                columns: new[] { "asset_id", "asset_version" });

            migrationBuilder.CreateIndex(
                name: "ix_media_usages_page_node_id",
                table: "media_usages",
                column: "page_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_usages_page_node_version_id",
                table: "media_usages",
                column: "page_node_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_usages");

            migrationBuilder.DropTable(
                name: "media_asset_versions");

            migrationBuilder.DropTable(
                name: "media_assets");
        }
    }
}
