using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCheckingExerciseStageAndDataType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outputs previously saved under a custom suffix must be regenerated using the
            // exercise type. Keep all input files and clear only their obsolete validation stamp.
            migrationBuilder.Sql("""
                UPDATE "CheckingExercises"
                SET "Validated_ValidatedAt" = NULL,
                    "Validated_IngressValidationChecksum" = NULL,
                    "Validated_SchemaValidationChecksum" = NULL
                WHERE "UsesExerciseStorage" = TRUE AND "DataType" IS NOT NULL
                  AND "DataType" <> CASE "ExerciseType"
                      WHEN 'PupilData' THEN 'Pupil' WHEN 'ResultsEnquiry' THEN 'Results' END;
                """);

            migrationBuilder.DropColumn(
                name: "DataType",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "CheckingExercises");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "CheckingExercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
