using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestAmendmentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent rather than AddColumn: an environment whose schema has already
            // diverged (the column added out of band) would otherwise fail here, and this
            // migration must be a no-op there.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ChangeRequests"
                ADD COLUMN IF NOT EXISTS "AmendmentType" character varying(20) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ChangeRequests"
                DROP COLUMN IF EXISTS "AmendmentType";
                """);
        }
    }
}
