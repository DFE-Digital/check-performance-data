using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendPupilIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pupils_CheckingWindowId_Laestab_Pincl",
                table: "Pupils");

            migrationBuilder.CreateIndex(
                name: "IX_Pupils_CheckingWindowId_Laestab_Pincl_Surname_Firstname",
                table: "Pupils",
                columns: new[] { "CheckingWindowId", "Laestab", "Pincl", "Surname", "Firstname" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pupils_CheckingWindowId_Laestab_Pincl_Surname_Firstname",
                table: "Pupils");

            migrationBuilder.CreateIndex(
                name: "IX_Pupils_CheckingWindowId_Laestab_Pincl",
                table: "Pupils",
                columns: new[] { "CheckingWindowId", "Laestab", "Pincl" });
        }
    }
}
