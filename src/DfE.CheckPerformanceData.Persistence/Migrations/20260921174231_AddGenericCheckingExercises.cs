using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericCheckingExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            // IF NOT EXISTS: idempotent, so it no-ops on a database that already has the change
            // (CLAUDE.md, "make the new migration idempotent so it no-ops on databases that
            // already have the change") — a review-app database must not fail the deploy.
            migrationBuilder.Sql("""
                ALTER TABLE "CheckingExercises" ADD COLUMN IF NOT EXISTS "Name" character varying(200) NOT NULL DEFAULT '';
                ALTER TABLE "CheckingExercises" ADD COLUMN IF NOT EXISTS "TabName" character varying(100) NOT NULL DEFAULT '';
                """);

            // Every existing row has a kind, so every existing row can be named from it.
            migrationBuilder.Sql("""
                UPDATE "CheckingExercises" SET "Name" = 'Pupil data checking', "TabName" = 'Pupils'
                    WHERE "ExerciseType" = 'PupilData' AND "Name" = '';
                UPDATE "CheckingExercises" SET "Name" = 'Results enquiry', "TabName" = 'Results'
                    WHERE "ExerciseType" = 'ResultsEnquiry' AND "Name" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true,
                filter: "\"ExerciseType\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback limitation: AlterColumn(nullable: false, defaultValue: "") writes '' into any
            // display-only (null-kind) row, so two of them in one window trip the unfiltered unique
            // index and a lone one becomes a value outside the enum. The old model has no
            // representation for a display-only exercise, so Down is only clean before any such
            // row exists.
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "TabName",
                table: "CheckingExercises");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true);
        }
    }
}
