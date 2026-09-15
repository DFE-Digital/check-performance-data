using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingExerciseCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "CheckingExercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

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
                name: "Stage",
                table: "CheckingExercises",
                type: "character varying(100)",
                maxLength: 100,
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

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExercises_ReplacesCheckingExerciseId",
                table: "CheckingExercises",
                column: "ReplacesCheckingExerciseId");

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
                name: "IX_CheckingExercises_ReplacesCheckingExerciseId",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "DataType",
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
                name: "Stage",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "TabName",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "TabOrder",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "VisibleFrom",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "VisibleUntil",
                table: "CheckingExercises");
        }
    }
}
