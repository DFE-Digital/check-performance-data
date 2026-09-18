using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataEgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganisationLaestab",
                table: "ChangeRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "egress_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedById = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreprocessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransferredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferredByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureJson = table.Column<string>(type: "text", nullable: true),
                    TransferFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_egress_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_egress_runs_CheckingWindows_WindowId",
                        column: x => x.WindowId,
                        principalTable: "CheckingWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "egress_run_outputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutputType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RawRecordsJson = table.Column<string>(type: "text", nullable: false),
                    SourceRecordCount = table.Column<int>(type: "integer", nullable: false),
                    OutputRecordCount = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_egress_run_outputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_egress_run_outputs_egress_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "egress_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "new_learners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CorrectionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrectionType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KeyStage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalAuthority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EstablishmentNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Surname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Forename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sex = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DateOfBirth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdmissionDate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Postcode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CycleYear = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CycleMonth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchoolUrn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Uln = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Upn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    YearGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_new_learners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_new_learners_egress_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "egress_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "remove_learners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CorrectionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrectionType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrectionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KeyStage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EstablishmentNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Surname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Forename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sex = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DateOfBirth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CycleYear = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CycleMonth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalAuthority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remove_learners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_remove_learners_egress_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "egress_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_egress_run_outputs_active_window_output",
                table: "egress_run_outputs",
                columns: new[] { "WindowId", "OutputType" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_egress_run_outputs_RunId",
                table: "egress_run_outputs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_egress_runs_StartedAtUtc",
                table: "egress_runs",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_egress_runs_WindowId",
                table: "egress_runs",
                column: "WindowId");

            migrationBuilder.CreateIndex(
                name: "IX_new_learners_RunId",
                table: "new_learners",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_remove_learners_RunId",
                table: "remove_learners",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "egress_run_outputs");

            migrationBuilder.DropTable(
                name: "new_learners");

            migrationBuilder.DropTable(
                name: "remove_learners");

            migrationBuilder.DropTable(
                name: "egress_runs");

            migrationBuilder.DropColumn(
                name: "OrganisationLaestab",
                table: "ChangeRequests");
        }
    }
}
