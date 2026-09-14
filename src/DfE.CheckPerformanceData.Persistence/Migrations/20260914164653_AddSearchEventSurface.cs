using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchEventSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host_path",
                table: "search_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "results_sections",
                table: "search_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "selected_key",
                table: "search_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "selected_position",
                table: "search_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "surface",
                table: "search_events",
                type: "text",
                nullable: false,
                defaultValue: "site");

            migrationBuilder.AlterColumn<bool>(
                name: "zero_results",
                table: "search_events",
                type: "boolean",
                nullable: false,
                computedColumnSql: "(results_pages + results_blocks + results_sections) = 0",
                stored: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComputedColumnSql: "(results_pages + results_blocks) = 0",
                oldStored: true);

            migrationBuilder.AlterColumn<int>(
                name: "results_total",
                table: "search_events",
                type: "integer",
                nullable: false,
                computedColumnSql: "results_pages + results_blocks + results_sections",
                stored: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComputedColumnSql: "results_pages + results_blocks",
                oldStored: true);

            migrationBuilder.CreateIndex(
                name: "ix_search_events_host_path_occurred_at",
                table: "search_events",
                columns: new[] { "host_path", "occurred_at_utc" },
                filter: "host_path IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_surface_occurred_at",
                table: "search_events",
                columns: new[] { "surface", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The generated columns are reverted FIRST: while their expression still names
            // results_sections, Postgres will not let that column be dropped. EF emits the
            // drops first by default, which makes the generated rollback fail at the first
            // statement.
            migrationBuilder.AlterColumn<bool>(
                name: "zero_results",
                table: "search_events",
                type: "boolean",
                nullable: false,
                computedColumnSql: "(results_pages + results_blocks) = 0",
                stored: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComputedColumnSql: "(results_pages + results_blocks + results_sections) = 0",
                oldStored: true);

            migrationBuilder.AlterColumn<int>(
                name: "results_total",
                table: "search_events",
                type: "integer",
                nullable: false,
                computedColumnSql: "results_pages + results_blocks",
                stored: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComputedColumnSql: "results_pages + results_blocks + results_sections",
                oldStored: true);

            migrationBuilder.DropIndex(
                name: "ix_search_events_host_path_occurred_at",
                table: "search_events");

            migrationBuilder.DropIndex(
                name: "ix_search_events_surface_occurred_at",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "host_path",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "results_sections",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "selected_key",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "selected_position",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "surface",
                table: "search_events");
        }
    }
}
