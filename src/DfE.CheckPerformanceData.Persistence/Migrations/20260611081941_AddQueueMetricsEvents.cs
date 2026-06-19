using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueMetricsEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "queue_metrics_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    queue_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    rules_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latency_ms = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_metrics_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_queue_metrics_events_queue_recorded",
                table: "queue_metrics_events",
                columns: new[] { "queue_name", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_queue_metrics_events_recorded_at",
                table: "queue_metrics_events",
                column: "recorded_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_queue_metrics_events_reference",
                table: "queue_metrics_events",
                columns: new[] { "reference_number", "recorded_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_metrics_events");
        }
    }
}
