using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <summary>
    /// Adds the SOURCE tag a dataset stamps on its records and whether the slot must be filled, and
    /// gives every existing results-enquiry exercise the upload slots for its source files (#324).
    /// </summary>
    /// <remarks>
    /// SourceFile is the exact analogue of Included: Included stamps inclusion by file of origin,
    /// SourceFile stamps provenance by file of origin. Null on every pupil-data dataset, which is
    /// why it is nullable and backfills nothing on its own. Required defaults to true so every
    /// existing slot keeps today's rule — the exercise is not validatable until the slot is filled.
    ///
    /// The data half matters more than the columns. Dataset slots are only reconciled when a window
    /// is saved through WindowService, so without this every results-enquiry exercise already on a
    /// deployed environment would show "no ingress files to load" until an admin happened to
    /// re-save its window — and nobody could upload the results files the enquiry journey needs.
    /// Only empty slots are created, so an admin who has uploaded files keeps them, and the NOT
    /// EXISTS guard makes it idempotent.
    /// </remarks>
    public partial class AddDatasetSourceFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Required",
                table: "CheckingWindowDatasets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "CheckingWindowDatasets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // One empty slot per source file, named by the tag it stamps. The tags are verbatim
            // from AB#296999 and are a data contract with the ingestion pipeline — they must stay
            // in step with ResultsFileTags. Only the main file is required: the late, revised and
            // retention files land weeks apart and one may never land, so requiring them would mean
            // an exercise that can never be validated. KS2 is absent on purpose: it has no feed.
            migrationBuilder.Sql("""
                INSERT INTO "CheckingWindowDatasets"
                    ("Id", "CheckingExerciseId", "CheckingWindowId", "Name", "IngressFile",
                     "IngressFileChecksum", "SchemaFile", "SchemaFileChecksum", "Included",
                     "SourceFile", "Required", "SortOrder")
                SELECT gen_random_uuid(), e."Id", w."Id", s.tag, '', '', '', '', NULL, s.tag,
                       s.sort = 0, s.sort
                FROM "CheckingExercises" e
                JOIN "CheckingWindows" w ON w."Id" = e."CheckingWindowId"
                JOIN LATERAL (
                    SELECT t.tag, t.sort
                    FROM (VALUES
                        ('Post16',    '16to19_MAIN',      0),
                        ('Post16',    '16to19_LR1',       1),
                        ('Post16',    '16to19_LR2',       2),
                        ('Post16',    '16to19_Revised',   3),
                        ('Post16',    '16to19_Retention', 4),
                        ('KS4June',   'KS4_MAIN',         0),
                        ('KS4June',   'KS4_LR1',          1),
                        ('KS4June',   'KS4_LR2',          2),
                        ('KS4June',   'KS4_Revised',      3),
                        ('KS4Autumn', 'KS4_MAIN',         0),
                        ('KS4Autumn', 'KS4_LR1',          1),
                        ('KS4Autumn', 'KS4_LR2',          2),
                        ('KS4Autumn', 'KS4_Revised',      3)
                    ) AS t(window_type, tag, sort)
                    WHERE t.window_type = w."CheckingWindowType"
                ) s ON TRUE
                WHERE e."ExerciseType" = 'ResultsEnquiry'
                AND NOT EXISTS (
                    SELECT 1 FROM "CheckingWindowDatasets" d
                    WHERE d."CheckingExerciseId" = e."Id" AND d."Name" = s.tag
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only slots nobody has uploaded to are removed. A slot holding a file is data an admin
            // put there, and a rollback must not throw it away.
            migrationBuilder.Sql("""
                DELETE FROM "CheckingWindowDatasets" d
                USING "CheckingExercises" e
                WHERE d."CheckingExerciseId" = e."Id"
                AND e."ExerciseType" = 'ResultsEnquiry'
                AND d."IngressFile" = ''
                AND d."SchemaFile" = '';
                """);

            migrationBuilder.DropColumn(
                name: "Required",
                table: "CheckingWindowDatasets");

            migrationBuilder.DropColumn(
                name: "SourceFile",
                table: "CheckingWindowDatasets");
        }
    }
}
