using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The RequestType column was originally added by editing the already-shipped
            // AddChangeRequest migration in place, so environments where that migration had
            // already run never received the column. This idempotent add reconciles those
            // environments while no-opping on databases that already have it.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"RequestType\" character varying(100) NOT NULL DEFAULT '';");

            // The default only exists to backfill any pre-existing rows; the EF model declares
            // no default, so drop it to keep reconciled databases consistent with fresh ones.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ALTER COLUMN \"RequestType\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"RequestType\";");
        }
    }
}
