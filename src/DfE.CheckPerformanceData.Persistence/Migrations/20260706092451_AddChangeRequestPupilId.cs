using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestPupilId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent so it no-ops on any environment that already has the column.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"PupilId\" uuid;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"PupilId\";");
        }
    }
}
