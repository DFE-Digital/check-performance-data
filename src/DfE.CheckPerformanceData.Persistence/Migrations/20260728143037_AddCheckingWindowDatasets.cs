using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingWindowDatasets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckingWindowDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IngressFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IngressFileChecksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchemaFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SchemaFileChecksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingWindowDatasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingWindowDatasets_CheckingWindows_CheckingWindowId",
                        column: x => x.CheckingWindowId,
                        principalTable: "CheckingWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckingWindowDatasets_CheckingWindowId_Name",
                table: "CheckingWindowDatasets",
                columns: new[] { "CheckingWindowId", "Name" },
                unique: true);

            // Every existing window becomes a single "pupils" dataset carrying the files it
            // already has, so windows created before this migration keep validating unchanged.
            // The NOT EXISTS guard makes the backfill idempotent, so re-running against a
            // database that already has rows is a no-op.
            migrationBuilder.Sql("""
                INSERT INTO "CheckingWindowDatasets"
                    ("Id", "CheckingWindowId", "Name", "IngressFile", "IngressFileChecksum",
                     "SchemaFile", "SchemaFileChecksum", "Included", "SortOrder")
                SELECT gen_random_uuid(), w."Id", 'pupils',
                       COALESCE(w."IngressFile", ''), COALESCE(w."IngressFileChecksum", ''),
                       COALESCE(w."SchemaFile", ''), COALESCE(w."SchemaFileChecksum", ''),
                       NULL, 0
                FROM "CheckingWindows" w
                WHERE NOT EXISTS (
                    SELECT 1 FROM "CheckingWindowDatasets" d WHERE d."CheckingWindowId" = w."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckingWindowDatasets");
        }
    }
}
