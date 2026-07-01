using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentId_PagesAndBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContentId",
                table: "WikiPages",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentId",
                table: "ContentBlocks",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_ContentId",
                table: "WikiPages",
                column: "ContentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlocks_ContentId",
                table: "ContentBlocks",
                column: "ContentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WikiPages_ContentId",
                table: "WikiPages");

            migrationBuilder.DropIndex(
                name: "IX_ContentBlocks_ContentId",
                table: "ContentBlocks");

            migrationBuilder.DropColumn(
                name: "ContentId",
                table: "WikiPages");

            migrationBuilder.DropColumn(
                name: "ContentId",
                table: "ContentBlocks");
        }
    }
}
