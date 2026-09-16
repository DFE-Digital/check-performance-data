using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeDisplayOnlyExerciseTypeOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CheckingExercises_TypeOrDisplayOnly",
                table: "CheckingExercises",
                sql: "\"ExerciseType\" IS NOT NULL OR (\"DisplayOnly\" AND \"UsesExerciseStorage\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A rollback must not invent a journey type for existing Summary exercises.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "CheckingExercises" WHERE "ExerciseType" IS NULL) THEN
                        RAISE EXCEPTION 'Assign exercise types before rolling back this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_CheckingExercises_TypeOrDisplayOnly",
                table: "CheckingExercises");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
