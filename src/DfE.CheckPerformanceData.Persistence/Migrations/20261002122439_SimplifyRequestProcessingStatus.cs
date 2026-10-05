using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyRequestProcessingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #536: one job per status column. Status says what the school did; ProcessingStatus
            // (was WorkerStatus) says where the request is on its way to a Zendesk ticket.
            migrationBuilder.RenameColumn(
                name: "WorkerStatus",
                table: "ChangeRequests",
                newName: "ProcessingStatus");

            migrationBuilder.Sql("""
                UPDATE "ChangeRequests" SET "Status" = 'Submitted'
                WHERE "Status" IN ('SubmittedUnCommitted', 'SubmittedCommitted');

                UPDATE "ChangeRequests" SET "ProcessingStatus" = CASE "ProcessingStatus"
                    WHEN 'RulesProcessed' THEN 'Decided'
                    WHEN 'ZendeskTicketCreating' THEN 'TicketCreating'
                    WHEN 'ZendeskTicketCreated' THEN 'TicketCreated'
                    ELSE "ProcessingStatus" END
                WHERE "ProcessingStatus" IS NOT NULL;

                -- A results enquiry is queued for its ticket at submit and never passes the Rules
                -- Engine. Under the new claim rule only TicketQueued is claimable, so an enquiry
                -- still waiting for its ticket must say so.
                UPDATE "ChangeRequests"
                SET "ProcessingStatus" = 'TicketQueued', "Outcome" = COALESCE("Outcome", 'Scrutiny')
                WHERE "RequestType" = 'ResultsEnquiry'
                  AND "Status" = 'Submitted'
                  AND "ProcessingStatus" IS NULL
                  AND "CrmId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy on purpose: Submitted cannot be split back into the two values it replaced, so
            // every submitted row returns as SubmittedUnCommitted, and an enquiry's TicketQueued
            // returns as null (the old "claimable enquiry" state).
            migrationBuilder.Sql("""
                UPDATE "ChangeRequests" SET "Status" = 'SubmittedUnCommitted' WHERE "Status" = 'Submitted';

                UPDATE "ChangeRequests" SET "ProcessingStatus" = CASE "ProcessingStatus"
                    WHEN 'Decided' THEN 'RulesProcessed'
                    WHEN 'TicketCreating' THEN 'ZendeskTicketCreating'
                    WHEN 'TicketCreated' THEN 'ZendeskTicketCreated'
                    WHEN 'TicketQueued' THEN NULL
                    ELSE "ProcessingStatus" END
                WHERE "ProcessingStatus" IS NOT NULL;
                """);

            migrationBuilder.RenameColumn(
                name: "ProcessingStatus",
                table: "ChangeRequests",
                newName: "WorkerStatus");
        }
    }
}
