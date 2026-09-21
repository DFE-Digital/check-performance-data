using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignEgressWithLdsSpecV24 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "new_learners");

            migrationBuilder.DropColumn(
                name: "SenStatus",
                table: "new_learners");

            migrationBuilder.AddColumn<string>(
                name: "RemovalYear0",
                table: "remove_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemovalYear1",
                table: "remove_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemovalYear2",
                table: "remove_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "YearGroup",
                table: "remove_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemovalYear0",
                table: "remove_learners");

            migrationBuilder.DropColumn(
                name: "RemovalYear1",
                table: "remove_learners");

            migrationBuilder.DropColumn(
                name: "RemovalYear2",
                table: "remove_learners");

            migrationBuilder.DropColumn(
                name: "YearGroup",
                table: "remove_learners");

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "new_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SenStatus",
                table: "new_learners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
