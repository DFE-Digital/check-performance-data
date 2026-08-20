using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <summary>
    /// Gives every existing 16-19 window the ResultsEnquiry exercise it is already behaving as if
    /// it had (#317).
    /// </summary>
    /// <remarks>
    /// Data only — no schema change. #313's backfill gave every window a PupilData row and nothing
    /// else, and #317 makes the check-your-pupil-data page offer "Report an issue with an exam
    /// result" only while a ResultsEnquiry exercise is open. Without this, the option would vanish
    /// from every deployed 16-19 window the moment #317 ships: a shipped feature silently withdrawn.
    ///
    /// The window's own dates are used, which reproduces exactly today's behaviour — the option is
    /// offered for the whole outer window. That is the thing #307 exists to fix, so this is
    /// deliberately transitional: #319's admin captures the real per-exercise dates, and this only
    /// has to hold the line until then.
    ///
    /// Post16 only. The other window types do not offer an enquiry today, and starting to offer one
    /// is a product decision for the admin screens, not for a migration.
    ///
    /// The NOT EXISTS guard makes it idempotent and, more importantly, non-destructive: a window
    /// someone has already configured with real enquiry dates keeps them.
    /// </remarks>
    public partial class BackfillResultsEnquiryExercise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "CheckingExercises"
                    ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
                SELECT gen_random_uuid(), w."Id", 'ResultsEnquiry', w."StartDate", w."EndDate", 1
                FROM "CheckingWindows" w
                WHERE w."CheckingWindowType" = 'Post16'
                AND NOT EXISTS (
                    SELECT 1 FROM "CheckingExercises" e
                    WHERE e."CheckingWindowId" = w."Id" AND e."ExerciseType" = 'ResultsEnquiry'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes only the rows this migration's shape produces. A row someone edited to real
            // enquiry dates no longer matches the window's own dates, so it survives a rollback.
            migrationBuilder.Sql("""
                DELETE FROM "CheckingExercises" e
                USING "CheckingWindows" w
                WHERE e."CheckingWindowId" = w."Id"
                AND e."ExerciseType" = 'ResultsEnquiry'
                AND w."CheckingWindowType" = 'Post16'
                AND e."StartDate" = w."StartDate"
                AND e."EndDate" = w."EndDate"
                AND e."SortOrder" = 1;
                """);
        }
    }
}
