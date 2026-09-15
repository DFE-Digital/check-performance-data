using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCheckingExerciseStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.AddColumn<bool>(
                name: "UsesExerciseStorage",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "UsesExerciseStorage",
                table: "CheckingExercises");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true);
        }
    }
}
