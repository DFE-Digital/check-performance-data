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
            // Outcome, OutcomeKey and MatchedRuleId already exist on environments that applied main's
            // AddChangeRequestOutcome migration (Dev, QA) before this branch's earlier-timestamped
            // migrations run. EF applies a pending migration even when its timestamp predates one already
            // recorded in the history, so this runs after those columns exist on Dev/QA — add them only if
            // absent, otherwise a plain AddColumn fails with "column already exists". Fresh databases
            // create them here. The decision outcome is persisted in these canonical columns; the branch
            // does not carry a separate DecisionStatus/DecisionOutcomeKey pair.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"Outcome\" character varying(20);");
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"OutcomeKey\" character varying(100);");
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"MatchedRuleId\" character varying(100);");

            // RulesVersion and DecidedAtUtc are new on every environment (main did not add them).
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                table: "ChangeRequests",
                type: "timestamp with time zone",
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
                name: "RulesVersion",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "ChangeRequests");

            // Mirror the idempotent Up: drop only if present, so a rollback is safe regardless of whether
            // main's AddChangeRequestOutcome or this migration created the column.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"MatchedRuleId\";");
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"OutcomeKey\";");
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"Outcome\";");
        }
    }
}
