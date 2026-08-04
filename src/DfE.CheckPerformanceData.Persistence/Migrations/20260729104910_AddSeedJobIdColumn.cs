using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedJobIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "job_id",
                table: "search_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_id",
                table: "search_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_id",
                table: "search_event_results",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_search_messages_job_id",
                table: "search_messages",
                column: "job_id",
                filter: "job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_job_id",
                table: "search_events",
                column: "job_id",
                filter: "job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_search_event_results_job_id",
                table: "search_event_results",
                column: "job_id",
                filter: "job_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_search_messages_job_id",
                table: "search_messages");

            migrationBuilder.DropIndex(
                name: "ix_search_events_job_id",
                table: "search_events");

            migrationBuilder.DropIndex(
                name: "ix_search_event_results_job_id",
                table: "search_event_results");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "search_messages");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "search_events");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "search_event_results");
        }
    }
}
