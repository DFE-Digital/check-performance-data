using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormaliseRequestType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The existing "RequestType" column holds the free-text description (e.g.
            // "Remove - pupil-died"). Rename it to "RequestTypeDescription" so its data is
            // preserved — an AlterColumn down to varchar(20) would truncate/fail. Postgres
            // RENAME COLUMN has no IF EXISTS, so guard with a DO block for idempotency.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'ChangeRequests' AND column_name = 'RequestType')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'ChangeRequests' AND column_name = 'RequestTypeDescription')
                    THEN
                        ALTER TABLE "ChangeRequests" RENAME COLUMN "RequestType" TO "RequestTypeDescription";
                    END IF;
                END $$;
                """);

            // Add the new enum-backed "RequestType" column. The DEFAULT only backfills
            // existing rows to 'Amendment' (no reclassification of old "Confirm Pupil Data
            // Declaration" rows); drop it afterwards so the EF model (no default) and fresh
            // databases stay consistent — same pattern as AddChangeRequestType.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"RequestType\" character varying(20) NOT NULL DEFAULT 'Amendment';");
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ALTER COLUMN \"RequestType\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"RequestType\";");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'ChangeRequests' AND column_name = 'RequestTypeDescription')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'ChangeRequests' AND column_name = 'RequestType')
                    THEN
                        ALTER TABLE "ChangeRequests" RENAME COLUMN "RequestTypeDescription" TO "RequestType";
                    END IF;
                END $$;
                """);
        }
    }
}
