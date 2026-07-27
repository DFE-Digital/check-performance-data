using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchAnalyticsPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_search_events_occurred_at_query_normalised",
                table: "search_events",
                columns: new[] { "occurred_at_utc", "query_normalised" },
                filter: "query_normalised IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_occurred_at_session_id",
                table: "search_events",
                columns: new[] { "occurred_at_utc", "session_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_search_events_occurred_at_query_normalised",
                table: "search_events");

            migrationBuilder.DropIndex(
                name: "ix_search_events_occurred_at_session_id",
                table: "search_events");
        }
    }
}
