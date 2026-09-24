using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingExerciseReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentReleaseId",
                table: "CheckingExercises",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckingExerciseReleaseId",
                table: "ChangeRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckingExerciseReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    FilesWritten = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingExerciseReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingExerciseReleases_CheckingExercises_CheckingExercise~",
                        column: x => x.CheckingExerciseId,
                        principalTable: "CheckingExercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckingExerciseReleaseFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingExerciseReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: true),
                    SourceFile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IngressFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IngressFileChecksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchemaFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SchemaFileChecksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingExerciseReleaseFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingExerciseReleaseFiles_CheckingExerciseReleases_Check~",
                        column: x => x.CheckingExerciseReleaseId,
                        principalTable: "CheckingExerciseReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExerciseReleaseFiles_CheckingExerciseReleaseId",
                table: "CheckingExerciseReleaseFiles",
                column: "CheckingExerciseReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExerciseReleases_CheckingExerciseId_Number",
                table: "CheckingExerciseReleases",
                columns: new[] { "CheckingExerciseId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckingExerciseReleaseFiles");

            migrationBuilder.DropTable(
                name: "CheckingExerciseReleases");

            migrationBuilder.DropColumn(
                name: "CurrentReleaseId",
                table: "CheckingExercises");

            migrationBuilder.DropColumn(
                name: "CheckingExerciseReleaseId",
                table: "ChangeRequests");
        }
    }
}
