using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    query_raw = table.Column<string>(type: "text", nullable: true),
                    query_normalised = table.Column<string>(type: "text", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: true),
                    results_pages = table.Column<int>(type: "integer", nullable: false),
                    results_blocks = table.Column<int>(type: "integer", nullable: false),
                    results_total = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "results_pages + results_blocks", stored: true),
                    zero_results = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "(results_pages + results_blocks) = 0", stored: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    what_looking_for = table.Column<string>(type: "text", nullable: false),
                    what_got = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_by_admin_sub = table.Column<string>(type: "text", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_event_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    search_event_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    result_kind = table.Column<string>(type: "text", nullable: false),
                    result_key = table.Column<string>(type: "text", nullable: false),
                    rank = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_event_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_event_results_search_events_search_event_id",
                        column: x => x.search_event_id,
                        principalTable: "search_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_event_results_search_event_id",
                table: "search_event_results",
                column: "search_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_occurred_at",
                table: "search_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_query_normalised",
                table: "search_events",
                column: "query_normalised");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_session_id",
                table: "search_events",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_events_zero_results_occurred_at",
                table: "search_events",
                columns: new[] { "zero_results", "occurred_at_utc" },
                filter: "zero_results = true");

            migrationBuilder.CreateIndex(
                name: "ix_search_messages_is_read",
                table: "search_messages",
                column: "is_read");

            migrationBuilder.CreateIndex(
                name: "ix_search_messages_session_id",
                table: "search_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_messages_submitted_at",
                table: "search_messages",
                column: "submitted_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_event_results");

            migrationBuilder.DropTable(
                name: "search_messages");

            migrationBuilder.DropTable(
                name: "search_events");
        }
    }
}
