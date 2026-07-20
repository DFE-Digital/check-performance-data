using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeywordsAndSearchVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Keywords",
                table: "PageNodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Keywords",
                table: "ContentBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValuePlainText",
                table: "ContentBlocks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "PageNodeVersions",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(\"BodyPlainText\", '')), 'D')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "PageNodes",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(\"Keywords\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"Title\", '')), 'B') || setweight(to_tsvector('english', coalesce(\"Subtitle\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "ContentBlocks",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(\"Keywords\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"ValuePlainText\", '')), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageNodeVersions_SearchVector",
                table: "PageNodeVersions",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_PageNodes_SearchVector",
                table: "PageNodes",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlocks_SearchVector",
                table: "ContentBlocks",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PageNodeVersions_SearchVector",
                table: "PageNodeVersions");

            migrationBuilder.DropIndex(
                name: "IX_PageNodes_SearchVector",
                table: "PageNodes");

            migrationBuilder.DropIndex(
                name: "IX_ContentBlocks_SearchVector",
                table: "ContentBlocks");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "PageNodeVersions");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "PageNodes");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "ContentBlocks");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "PageNodes");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "ContentBlocks");

            migrationBuilder.DropColumn(
                name: "ValuePlainText",
                table: "ContentBlocks");
        }
    }
}
