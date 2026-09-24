using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <summary>
    /// Every checking exercise has a tab name. A null tab name used to mean "configured before
    /// #466", which exempted the row from IsEnabled and the visibility dates. That exemption is
    /// gone, so each such row gets its kind's default tab name and is enabled here, or schools
    /// would lose data they can see today.
    /// </summary>
    /// <remarks>
    /// A row is enabled only if no other enabled row of the same kind exists in its window. A
    /// window must never have two live exercises of one kind, so a clash stays disabled for an
    /// admin to resolve. The default names match <c>WindowExercises.DefaultTabName</c>.
    /// </remarks>
    public partial class RequireExerciseTabName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CheckingExercises" AS e
                SET "TabName" = CASE
                        WHEN e."ExerciseType" = 'ResultsEnquiry' THEN 'Results'
                        WHEN e."ExerciseType" = 'PupilData' AND w."CheckingWindowType" = 'Post16' THEN 'Students'
                        WHEN e."ExerciseType" = 'PupilData' THEN 'Pupils'
                        ELSE COALESCE(NULLIF(e."Name", ''), 'Data')
                    END,
                    "IsEnabled" = NOT EXISTS (
                        SELECT 1 FROM "CheckingExercises" AS other
                        WHERE other."CheckingWindowId" = e."CheckingWindowId"
                          AND other."Id" <> e."Id"
                          AND other."ExerciseType" = e."ExerciseType"
                          AND other."IsEnabled")
                FROM "CheckingWindows" AS w
                WHERE w."Id" = e."CheckingWindowId" AND e."TabName" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TabName",
                table: "CheckingExercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TabName",
                table: "CheckingExercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
