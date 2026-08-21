using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <summary>
    /// A dataset is an input to one exercise, not to the window (#314). Every dataset row in the
    /// database today serves its window's single pupil-data activity, so each is pointed at the
    /// PupilData exercise the previous migration backfilled for that window.
    /// </summary>
    /// <remarks>
    /// CheckingWindowId is deliberately left in place. The previous release reads datasets through
    /// it, so keeping the column and its values is what makes a rollback safe. A follow-up
    /// migration drops it once every environment has moved.
    /// </remarks>
    public partial class ReparentDatasetsOntoCheckingExercise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckingWindowDatasets_CheckingWindows_CheckingWindowId",
                table: "CheckingWindowDatasets");

            migrationBuilder.DropIndex(
                name: "IX_CheckingWindowDatasets_CheckingWindowId_Name",
                table: "CheckingWindowDatasets");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckingExerciseId",
                table: "CheckingWindowDatasets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // A window created between this migration and the one before it has dataset rows but
            // no exercise, because the previous release still wrote datasets straight to the
            // window. Backfill the same single PupilData exercise that migration did, so those
            // rows have a parent. Idempotent: a window that already has one is skipped.
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

            // Point every existing row at its window's pupil-data exercise. This must run before
            // the foreign key below, or the all-zero default would violate it. Restricted to rows
            // that have not been repointed already, so re-running it is a no-op.
            migrationBuilder.Sql("""
                UPDATE "CheckingWindowDatasets" d
                SET "CheckingExerciseId" = e."Id"
                FROM "CheckingExercises" e
                WHERE e."CheckingWindowId" = d."CheckingWindowId"
                  AND e."ExerciseType" = 'PupilData'
                  AND d."CheckingExerciseId" = '00000000-0000-0000-0000-000000000000';
                """);

            // Nothing should be left unpointed after the two statements above. Fail loudly rather
            // than let the foreign key below report it as an opaque constraint violation.
            migrationBuilder.Sql("""
                DO $$
                DECLARE orphans bigint;
                BEGIN
                    SELECT count(*) INTO orphans FROM "CheckingWindowDatasets"
                    WHERE "CheckingExerciseId" = '00000000-0000-0000-0000-000000000000';

                    IF orphans > 0 THEN
                        RAISE EXCEPTION
                            '% dataset row(s) have no PupilData exercise to hang off.', orphans;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CheckingWindowDatasets_CheckingExerciseId_Name",
                table: "CheckingWindowDatasets",
                columns: new[] { "CheckingExerciseId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CheckingWindowDatasets_CheckingExercises_CheckingExerciseId",
                table: "CheckingWindowDatasets",
                column: "CheckingExerciseId",
                principalTable: "CheckingExercises",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckingWindowDatasets_CheckingExercises_CheckingExerciseId",
                table: "CheckingWindowDatasets");

            migrationBuilder.DropIndex(
                name: "IX_CheckingWindowDatasets_CheckingExerciseId_Name",
                table: "CheckingWindowDatasets");

            // CheckingWindowId was never cleared, so dropping the new column restores exactly the
            // shape the previous release reads.
            migrationBuilder.DropColumn(
                name: "CheckingExerciseId",
                table: "CheckingWindowDatasets");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingWindowDatasets_CheckingWindowId_Name",
                table: "CheckingWindowDatasets",
                columns: new[] { "CheckingWindowId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CheckingWindowDatasets_CheckingWindows_CheckingWindowId",
                table: "CheckingWindowDatasets",
                column: "CheckingWindowId",
                principalTable: "CheckingWindows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
