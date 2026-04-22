using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPupilsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AmendmentRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationUrn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStepIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmendmentRequest", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pupils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laestab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Surname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Firstname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sex = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    DateOfBirth = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    FirstLanguage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Pincl = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pupils", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AmendmentRequestStepResponse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AmendmentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    ResponseData = table.Column<string>(type: "jsonb", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmendmentRequestStepResponse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmendmentRequestStepResponse_AmendmentRequest_AmendmentRequ~",
                        column: x => x.AmendmentRequestId,
                        principalTable: "AmendmentRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AmendmentRequestStepResponse_AmendmentRequestId",
                table: "AmendmentRequestStepResponse",
                column: "AmendmentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Pupils_CheckingWindowId_Laestab_Pincl",
                table: "Pupils",
                columns: new[] { "CheckingWindowId", "Laestab", "Pincl" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AmendmentRequestStepResponse");

            migrationBuilder.DropTable(
                name: "Pupils");

            migrationBuilder.DropTable(
                name: "AmendmentRequest");
        }
    }
}
