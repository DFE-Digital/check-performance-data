using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganisationLogins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrganisationUrn = table.Column<long>(type: "bigint", nullable: false),
                    Laestab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrganisationName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LoggedInAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationLogins", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationLogins_LoggedInAtUtc",
                table: "OrganisationLogins",
                column: "LoggedInAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationLogins_OrganisationUrn_LoggedInAtUtc",
                table: "OrganisationLogins",
                columns: new[] { "OrganisationUrn", "LoggedInAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationLogins");
        }
    }
}
