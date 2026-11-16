using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestEnquirySubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnquiryQan",
                table: "ChangeRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnquirySession",
                table: "ChangeRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnquiryQan",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "EnquirySession",
                table: "ChangeRequests");
        }
    }
}
