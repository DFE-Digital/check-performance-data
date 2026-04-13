using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckingWindow_KeyStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OrganisationType",
                table: "CheckingWindows",
                newName: "KeyStage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "KeyStage",
                table: "CheckingWindows",
                newName: "OrganisationType");
        }
    }
}
