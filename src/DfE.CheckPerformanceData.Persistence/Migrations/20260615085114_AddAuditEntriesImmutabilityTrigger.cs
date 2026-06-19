using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntriesImmutabilityTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enforce an INSERT-only forensic audit at the database. A BEFORE UPDATE OR DELETE
            // trigger raises, so even a role that can purge dead letters cannot rewrite or erase
            // the trail of that purge — the audit cannot be tampered with from application code.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION prevent_audit_entry_mutation()
RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'AuditEntries is immutable: % is not permitted', TG_OP;
END;
$$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_audit_entries_immutable
BEFORE UPDATE OR DELETE ON ""AuditEntries""
FOR EACH ROW EXECUTE FUNCTION prevent_audit_entry_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_audit_entries_immutable ON ""AuditEntries"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS prevent_audit_entry_mutation();");
        }
    }
}
