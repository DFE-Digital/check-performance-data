using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentBlockMediaUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "page_node_version_id",
                table: "media_usages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "page_node_id",
                table: "media_usages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "content_block_id",
                table: "media_usages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_usages_content_block_id",
                table: "media_usages",
                column: "content_block_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_usages_owner",
                table: "media_usages",
                sql: "(page_node_version_id IS NOT NULL AND page_node_id IS NOT NULL AND content_block_id IS NULL) OR (page_node_version_id IS NULL AND page_node_id IS NULL AND content_block_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_media_usages_content_blocks",
                table: "media_usages",
                column: "content_block_id",
                principalTable: "ContentBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Block rows have no page, so they cannot stay once the page columns are required again.
            migrationBuilder.Sql("DELETE FROM media_usages WHERE content_block_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_media_usages_content_blocks",
                table: "media_usages");

            migrationBuilder.DropIndex(
                name: "ix_media_usages_content_block_id",
                table: "media_usages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_usages_owner",
                table: "media_usages");

            migrationBuilder.DropColumn(
                name: "content_block_id",
                table: "media_usages");

            migrationBuilder.AlterColumn<Guid>(
                name: "page_node_version_id",
                table: "media_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "page_node_id",
                table: "media_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
