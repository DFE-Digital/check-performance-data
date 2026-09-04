using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestDecisionTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Unbounded text on purpose: the engine caps the trace at MaxTraceLines lines but not
            // at any line width, so a varchar(n) would reject a long free-text leaf line. The
            // write site truncates instead. Idempotent for the same reason the neighbouring
            // ExtendChangeRequestDecisionFields migration is: this branch's decision columns have
            // diverged across Dev/QA before, and a plain AddColumn fails outright on a database
            // that already has the column.
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" ADD COLUMN IF NOT EXISTS \"DecisionTrace\" text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ChangeRequests\" DROP COLUMN IF EXISTS \"DecisionTrace\";");
        }
    }
}
