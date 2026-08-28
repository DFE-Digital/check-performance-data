using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckingExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingExercises_CheckingWindows_CheckingWindowId",
                        column: x => x.CheckingWindowId,
                        principalTable: "CheckingWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true);

            // Every window in the database today runs a single pupil-data activity across the
            // whole window, so each one becomes one PupilData exercise on its own dates. Nothing
            // reads this table yet — the readers arrive in #315 onwards. The NOT EXISTS guard
            // makes the backfill idempotent: re-running it skips a window that already has its
            // PupilData row, and still gives one to a window that only has other exercise types.
            migrationBuilder.Sql("""
                INSERT INTO "CheckingExercises"
                    ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
                SELECT gen_random_uuid(), w."Id", 'PupilData', w."StartDate", w."EndDate", 0
                FROM "CheckingWindows" w
                WHERE NOT EXISTS (
                    SELECT 1 FROM "CheckingExercises" e
                    WHERE e."CheckingWindowId" = w."Id" AND e."ExerciseType" = 'PupilData'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckingExercises");
        }
    }
}
