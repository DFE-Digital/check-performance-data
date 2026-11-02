using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestZendeskDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ZendeskDecision",
                table: "ChangeRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZendeskDecisionReason",
                table: "ChangeRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ZendeskDecision",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "ZendeskDecisionReason",
                table: "ChangeRequests");
        }
    }
}
