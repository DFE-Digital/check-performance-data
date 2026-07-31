using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequests_WithdrawnByEmailAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WithdrawnAt",
                table: "ChangeRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawnByEmail",
                table: "ChangeRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WithdrawnAt",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawnByEmail",
                table: "ChangeRequests");
        }
    }
}
