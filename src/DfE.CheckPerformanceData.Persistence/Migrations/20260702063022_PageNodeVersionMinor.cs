using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageNodeVersionMinor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinorVersion",
                table: "PageNodeVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: existing published rows (PublishFrom IS NOT NULL) get Minor=0 (integer versions).
            // Existing draft rows (PublishFrom IS NULL) get Minor=1 (draft, will increment on next save).
            migrationBuilder.Sql(
                "UPDATE \"PageNodeVersions\" " +
                "SET \"MinorVersion\" = CASE WHEN \"PublishFrom\" IS NULL THEN 1 ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinorVersion",
                table: "PageNodeVersions");
        }
    }
}
