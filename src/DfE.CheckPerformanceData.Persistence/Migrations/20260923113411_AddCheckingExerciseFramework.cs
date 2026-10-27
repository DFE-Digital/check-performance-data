using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingExerciseFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<bool>(
                name: "DisplayOnly",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CheckingExercises",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesCheckingExerciseId",
                table: "CheckingExercises",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TabName",
                table: "CheckingExercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TabOrder",
                table: "CheckingExercises",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Every row that exists now was written against the type-based blob layout. Defaulting
            // this to true would tell the reader to look under exercises/{id}/, where those rows have
            // nothing, and every school would see an empty page.
            migrationBuilder.AddColumn<bool>(
                name: "UsesExerciseStorage",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisibleFrom",
                table: "CheckingExercises",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisibleUntil",
                table: "CheckingExercises",
                type: "timestamp without time zone",
                nullable: true);

            // New rows use the new layout; only the backfill above is false.
            migrationBuilder.Sql("ALTER TABLE \"CheckingExercises\" ALTER COLUMN \"UsesExerciseStorage\" SET DEFAULT TRUE;");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true,
                filter: "\"UsesExerciseStorage\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_ReplacesCheckingExerciseId",
                table: "CheckingExercises",
                column: "ReplacesCheckingExerciseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CheckingExercises_TypeOrDisplayOnly",
                table: "CheckingExercises",
                sql: "\"ExerciseType\" IS NOT NULL OR (\"DisplayOnly\" AND \"UsesExerciseStorage\")");

            migrationBuilder.AddForeignKey(
                name: "FK_CheckingExercises_CheckingExercises_ReplacesCheckingExercis~",
                table: "CheckingExercises",
                column: "ReplacesCheckingExerciseId",
                principalTable: "CheckingExercises",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckingExercises_CheckingExercises_ReplacesCheckingExercis~",
                table: "CheckingExercises");

            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises");

            migrationBuilder.DropIndex(
                name: "IX_CheckingExercises_ReplacesCheckingExerciseId",
                table: "CheckingExercises");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CheckingExercises_TypeOrDisplayOnly",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "DisplayOnly",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "ReplacesCheckingExerciseId",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "TabName",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "TabOrder",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "UsesExerciseStorage",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "VisibleFrom",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "VisibleUntil",
                table: "CheckingExercises");

            migrationBuilder.AlterColumn<string>(
                name: "ExerciseType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_CheckingWindowId_ExerciseType",
                table: "CheckingExercises",
                columns: new[] { "CheckingWindowId", "ExerciseType" },
                unique: true);
        }
    }
}
