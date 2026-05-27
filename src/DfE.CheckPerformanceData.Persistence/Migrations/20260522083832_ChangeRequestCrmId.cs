using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequestCrmId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CrmId",
                table: "ChangeRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrmId",
                table: "ChangeRequests");
        }
    }
}
