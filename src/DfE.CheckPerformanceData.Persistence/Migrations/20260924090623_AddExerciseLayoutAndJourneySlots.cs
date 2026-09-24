using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseLayoutAndJourneySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FeedsJourney",
                table: "CheckingWindowDatasets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every slot that exists now on an exercise with a kind is a supplier slot: the ones
            // WindowDatasets.DefaultsFor creates, or the ones older migrations backfilled. Those feed
            // the journey. A display-only share (no kind) has none that do.
            migrationBuilder.Sql("""
                UPDATE "CheckingWindowDatasets" AS d
                SET "FeedsJourney" = true
                FROM "CheckingExercises" AS e
                WHERE d."CheckingExerciseId" = e."Id" AND e."ExerciseType" IS NOT NULL;
                """);

            migrationBuilder.AddColumn<string>(
                name: "Layout",
                table: "CheckingExercises",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Table");

            // Before this column the layout came from each schema's x-display.layout. The only
            // exercises that asked for "vertical" are the one-record-per-school summary shares,
            // which are display only. Setting it here keeps them as they look now; an admin can
            // check any other exercise on its edit page.
            migrationBuilder.Sql("""
                UPDATE "CheckingExercises"
                SET "Layout" = 'Vertical'
                WHERE "ExerciseType" IS NULL AND "TabName" = 'Summary';
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "DatasetId",
                table: "CheckingExerciseReleaseFiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            // Not backfilled on purpose. A release published before this migration wrote only the
            // merged file, with no per-dataset files. An empty DatasetId is how the reader knows to
            // read that release's merged file instead.

            migrationBuilder.AddColumn<bool>(
                name: "FeedsJourney",
                table: "CheckingExerciseReleaseFiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedsJourney",
                table: "CheckingWindowDatasets");

            migrationBuilder.DropColumn(
                name: "Layout",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "DatasetId",
                table: "CheckingExerciseReleaseFiles");

            migrationBuilder.DropColumn(
                name: "FeedsJourney",
                table: "CheckingExerciseReleaseFiles");
        }
    }
}
