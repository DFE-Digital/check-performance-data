using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayOnlyCheckingExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisplayOnly",
                table: "CheckingExercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayOnly",
                table: "CheckingExercises");
        }
    }
}
