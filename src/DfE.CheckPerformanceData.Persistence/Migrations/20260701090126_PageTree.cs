using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Segment = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    PageType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageNode_PageNode_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PageNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PageNodeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    PublishFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    BodyPlainText = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageNodeVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageNodeVersions_PageNodes_PageNodeId",
                        column: x => x.PageNodeId,
                        principalTable: "PageNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageNodes_ParentId",
                table: "PageNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PageNodes_Path",
                table: "PageNodes",
                column: "Path",
                unique: true,
                filter: "\"DeletedDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PageNodeVersions_PageNodeId_IsCurrent",
                table: "PageNodeVersions",
                columns: new[] { "PageNodeId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_PageNodeVersions_PageNodeId_VersionId",
                table: "PageNodeVersions",
                columns: new[] { "PageNodeId", "VersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageNodeVersions");

            migrationBuilder.DropTable(
                name: "PageNodes");
        }
    }
}
