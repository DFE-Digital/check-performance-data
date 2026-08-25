using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingWindowTurnaroundCommitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TurnaroundCommitment",
                table: "CheckingWindows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TurnaroundCommitment",
                table: "CheckingWindows");
        }
    }
}
