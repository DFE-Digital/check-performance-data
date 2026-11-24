using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPageVersionSupersededBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupersededById",
                table: "PageNodeVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageNodeVersions_SupersededById",
                table: "PageNodeVersions",
                column: "SupersededById");

            migrationBuilder.AddForeignKey(
                name: "FK_PageNodeVersions_PageNodeVersions_SupersededById",
                table: "PageNodeVersions",
                column: "SupersededById",
                principalTable: "PageNodeVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PageNodeVersions_PageNodeVersions_SupersededById",
                table: "PageNodeVersions");

            migrationBuilder.DropIndex(
                name: "IX_PageNodeVersions_SupersededById",
                table: "PageNodeVersions");

            migrationBuilder.DropColumn(
                name: "SupersededById",
                table: "PageNodeVersions");
        }
    }
}
