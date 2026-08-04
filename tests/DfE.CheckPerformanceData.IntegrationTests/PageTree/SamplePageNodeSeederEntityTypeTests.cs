using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Verifies the sample-page seeder writes to the CURRENT content-type entities
// (PageNode + PageNodeVersion) and NOT to the retired WikiPage / WikiPageVersion tables
// that the DropWikiPagePlumbing migration removed. Guards against a future regression
// where someone re-plumbs the seeder onto the old tables (there is no code path to do
// so today — the seeder goes through IPageNodeService — but pinning the invariant on
// a real Postgres schema catches the day someone would.
[Collection(nameof(PostgresCollection))]
public sealed class SamplePageNodeSeederEntityTypeTests
{
    private readonly PostgresFixture _fixture;

    public SamplePageNodeSeederEntityTypeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"" RESTART IDENTITY CASCADE;");
    }

    [Fact]
    public async Task SeedAsync_WritesPageNodeAndPageNodeVersion_NotWikiPages()
    {
        await ResetAsync();

        // Seed the four root nodes the sample catalogue references (/wiki, /help, /support,
        // /guidance). SamplePageNodeSeeder skips a whole root when its GetNodeByPathAsync
        // lookup returns null, so without these the assertion below would be trivially zero.
        await using (var ctx = _fixture.CreateContext())
        {
            var pageRepo = new PageNodeRepository(ctx);
            var pageSvc = new PageNodeService(pageRepo);
            foreach (var segment in new[] { "wiki", "help", "support", "guidance" })
            {
                await pageSvc.CreatePageAsync(null, segment, segment, "folder", userId: "seed");
            }
        }

        // Run the seeder against the same context factory as the production controller.
        await using (var ctx = _fixture.CreateContext())
        {
            var pageRepo = new PageNodeRepository(ctx);
            var pageSvc = new PageNodeService(pageRepo);
            var seeder = new SamplePageNodeSeeder(pageSvc);
            var created = await seeder.SeedAsync(userId: "seed-test");
            Assert.True(created > 0,
                $"Seeder should create at least one page under the four roots; got {created}.");
        }

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Content flows through PageNode / PageNodeVersion.
        var pageNodeCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"PageNodes\" WHERE \"ParentId\" IS NOT NULL;");
        var pageNodeVersionCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"PageNodeVersions\";");
        Assert.True(pageNodeCount >= 13,
            $"Expected at least 13 sample-page PageNode rows (4 wiki + 3+3+3 others); got {pageNodeCount}.");
        Assert.True(pageNodeVersionCount >= 13,
            $"Expected at least 13 PageNodeVersion rows (one working + one published per sample); got {pageNodeVersionCount}.");

        // Retired wiki tables must not exist in the current schema — the DropWikiPagePlumbing
        // migration removed them. Query information_schema.tables so the assertion works
        // whether the tables are absent (correct) or present (would flag a schema regression).
        var wikiPagesTableExists = await ScalarLongAsync(conn, @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('WikiPages', 'WikiPageVersions', 'wiki_pages', 'wiki_page_versions');");
        Assert.Equal(0L, wikiPagesTableExists);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
