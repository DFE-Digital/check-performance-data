using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pupils : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyPlainText",
                table: "WikiPages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "WikiPages",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"BodyPlainText\", '')), 'B')",
                stored: true);

            migrationBuilder.CreateTable(
                name: "Pupils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laestab = table.Column<string>(type: "text", nullable: false),
                    Surname = table.Column<string>(type: "text", nullable: false),
                    Firstname = table.Column<string>(type: "text", nullable: false),
                    Sex = table.Column<string>(type: "text", nullable: false),
                    DateOfBirth = table.Column<string>(type: "text", nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    FirstLanguage = table.Column<string>(type: "text", nullable: false),
                    Pincl = table.Column<int>(type: "integer", nullable: false),
                    NewMobile = table.Column<bool>(type: "boolean", nullable: false),
                    ActualYearGroup = table.Column<string>(type: "text", nullable: false),
                    Ethnicity = table.Column<string>(type: "text", nullable: false),
                    SenF = table.Column<string>(type: "text", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Urn = table.Column<string>(type: "text", nullable: false),
                    Cypmd_Id = table.Column<string>(type: "text", nullable: false),
                    MatchRef = table.Column<int>(type: "integer", nullable: false),
                    Upn = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pupils", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_SearchVector",
                table: "WikiPages",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pupils");

            migrationBuilder.DropIndex(
                name: "IX_WikiPages_SearchVector",
                table: "WikiPages");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "WikiPages");

            migrationBuilder.DropColumn(
                name: "BodyPlainText",
                table: "WikiPages");
        }
    }
}
