using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropExerciseSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TabOrder now orders the exercises everywhere. The wizard wrote only SortOrder, so its
            // rows all have TabOrder 0 and would tie. Give each such row the tab order the wizard
            // now writes (WindowExercises.DefaultTabOrder, written out because a migration must not
            // change when the code does), so pupil data stays before results enquiry. A row whose
            // admin set a tab order keeps it.
            migrationBuilder.Sql(
                """
                UPDATE "CheckingExercises"
                SET "TabOrder" = ("SortOrder" + 1) * 100
                WHERE "TabOrder" = 0 AND "ExerciseType" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "CheckingExercises");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "CheckingExercises",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
