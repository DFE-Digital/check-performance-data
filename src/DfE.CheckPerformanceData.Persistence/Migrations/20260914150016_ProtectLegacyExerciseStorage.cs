using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectLegacyExerciseStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true,
                filter: "\"UsesExerciseStorage\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" });
        }
    }
}
