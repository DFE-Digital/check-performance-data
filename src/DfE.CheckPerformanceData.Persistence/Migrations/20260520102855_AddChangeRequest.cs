using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationUrn = table.Column<long>(type: "bigint", nullable: false),
                    PupilUpn = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PupilFirstname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PupilSurname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Submitted = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_CheckingWindows_WindowId",
                        column: x => x.WindowId,
                        principalTable: "CheckingWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ReferenceNumber",
                table: "ChangeRequests",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_Status",
                table: "ChangeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_WindowId_OrganisationUrn",
                table: "ChangeRequests",
                columns: new[] { "WindowId", "OrganisationUrn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeRequests");
        }
    }
}
