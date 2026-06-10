using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendChangeRequestDecisionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                table: "ChangeRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionOutcomeKey",
                table: "ChangeRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchedRuleId",
                table: "ChangeRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesVersion",
                table: "ChangeRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "DecisionOutcomeKey",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "MatchedRuleId",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "RulesVersion",
                table: "ChangeRequests");
        }
    }
}
