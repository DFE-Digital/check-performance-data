using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingWindowSampleSchools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckingWindowSampleSchools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laestab = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Urn = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AddedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingWindowSampleSchools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingWindowSampleSchools_CheckingWindows_CheckingWindowId",
                        column: x => x.CheckingWindowId,
                        principalTable: "CheckingWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckingWindowSampleSchools_CheckingWindowId_Laestab",
                table: "CheckingWindowSampleSchools",
                columns: new[] { "CheckingWindowId", "Laestab" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckingWindowSampleSchools");
        }
    }
}
