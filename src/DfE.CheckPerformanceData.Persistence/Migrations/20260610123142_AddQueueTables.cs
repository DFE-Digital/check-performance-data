using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "queue_dead_letters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    queue_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    enqueued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dead_lettered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "queue_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    queue_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    enqueued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    visible_after_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_queue_dead_letters_dead_lettered_at",
                table: "queue_dead_letters",
                column: "dead_lettered_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_queue_messages_claim",
                table: "queue_messages",
                columns: new[] { "queue_name", "status", "visible_after_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_dead_letters");

            migrationBuilder.DropTable(
                name: "queue_messages");
        }
    }
}
