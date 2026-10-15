using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseShowLateResultsWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowLateResultsWarning",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The warning used to be derived: a results enquiry showed it while its second late
            // results slot was in use and the live release had not read it. Tick the box wherever
            // that is true today, so no school's journey changes when this ships. The literals are
            // ResultsFileTags.Post16LateResults2 and Ks4LateResults2, written out because a
            // migration must not change when the constants do.
            migrationBuilder.Sql(
                """
                UPDATE "CheckingExercises" e
                SET "ShowLateResultsWarning" = TRUE
                WHERE e."ExerciseType" = 'ResultsEnquiry'
                  AND EXISTS (
                      SELECT 1 FROM "CheckingWindowDatasets" d
                      WHERE d."CheckingExerciseId" = e."Id"
                        AND NOT d."Retired"
                        AND d."SourceFile" IN ('16to19_LR2', 'KS4_LR2')
                        AND NOT EXISTS (
                            SELECT 1 FROM "CheckingExerciseReleaseFiles" f
                            WHERE f."CheckingExerciseReleaseId" = e."CurrentReleaseId"
                              AND f."DatasetId" = d."Id"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowLateResultsWarning",
                table: "CheckingExercises");
        }
    }
}
