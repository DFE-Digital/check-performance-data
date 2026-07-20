using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    // Retires the WikiPages / WikiPageVersions corpus. One-way decommission: the entity,
    // configuration, repository, service, seeder and HTTP surface have all been removed
    // from the codebase, so an attempt to migrate Down() would need to resurrect them
    // before it could repopulate content. Throw instead of scaffolding a stale schema
    // that no code path can produce or consume.
    public partial class DropWikiPagePlumbing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WikiPageVersions");
            migrationBuilder.DropTable(name: "WikiPages");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "DropWikiPagePlumbing is a one-way migration. The wiki corpus has been " +
                "removed from the codebase; restore the deleted entities and mapping " +
                "before attempting a downgrade.");
        }
    }
}
