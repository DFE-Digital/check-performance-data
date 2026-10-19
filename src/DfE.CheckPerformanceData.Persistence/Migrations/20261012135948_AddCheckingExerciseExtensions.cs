using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckingExerciseExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckingExerciseExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckingExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestsSentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingExerciseExtensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingExerciseExtensions_CheckingExercises_CheckingExerci~",
                        column: x => x.CheckingExerciseId,
                        principalTable: "CheckingExercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckingExerciseExtensionSchools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckingExerciseExtensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckingExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laestab = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckingExerciseExtensionSchools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckingExerciseExtensionSchools_CheckingExerciseExtensions~",
                        column: x => x.CheckingExerciseExtensionId,
                        principalTable: "CheckingExerciseExtensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExerciseExtensions_CheckingExerciseId",
                table: "CheckingExerciseExtensions",
                column: "CheckingExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExerciseExtensionSchools_CheckingExerciseExtensionId",
                table: "CheckingExerciseExtensionSchools",
                column: "CheckingExerciseExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckingExerciseExtensionSchools_CheckingExerciseId_Laestab",
                table: "CheckingExerciseExtensionSchools",
                columns: new[] { "CheckingExerciseId", "Laestab" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckingExerciseExtensionSchools");

            migrationBuilder.DropTable(
                name: "CheckingExerciseExtensions");
        }
    }
}
