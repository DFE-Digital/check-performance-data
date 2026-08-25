using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Verifies the sample-page seeder writes to the CURRENT content-type entities
// (PageNode + PageNodeVersion) and NOT to the retired WikiPage / WikiPageVersion tables
// that the DropWikiPagePlumbing migration removed. Guards against a future regression
// where someone re-plumbs the seeder onto the old tables.
//
// Also the end-to-end proof that the shipped bundle actually seeds: the samples are data in an
// embedded file, so nothing at compile time guarantees they land as rows. Running the real
// importer against a real Postgres schema is the only thing that does.
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

        // Create the roots exactly as production start-up does. This has to be DefaultPageNodeSeeder
        // rather than ad-hoc CreatePageAsync calls: the bundle references its parents by the pinned
        // Guids in DefaultPageNodeRoots, so roots created with fresh Guids would leave every sample
        // an orphan and the seed would silently do nothing.
        await using (var ctx = _fixture.CreateContext())
        {
            var pageRepo = new PageNodeRepository(ctx);
            await new DefaultPageNodeSeeder(new PageNodeService(pageRepo), pageRepo).SeedAsync();
        }

        int created;
        await using (var ctx = _fixture.CreateContext())
        {
            var seeder = new SamplePageNodeSeeder(BuildStaging(ctx));
            created = await seeder.SeedAsync();
            Assert.True(created >= 13,
                $"Seeder should create every sample page under the four roots; got {created}.");
        }

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Content flows through PageNode / PageNodeVersion.
        var pageNodeCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"PageNodes\" WHERE \"ParentId\" IS NOT NULL;");
        var pageNodeVersionCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"PageNodeVersions\";");
        Assert.True(pageNodeCount >= 13,
            $"Expected at least 13 sample-page PageNode rows; got {pageNodeCount}.");
        Assert.True(pageNodeVersionCount >= 13,
            $"Expected at least 13 PageNodeVersion rows; got {pageNodeVersionCount}.");

        // Every sample must end up published, or it 404s on the front end and the samples are
        // useless for browsing and for the browser tests that navigate to them.
        var publishedCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"PageNodeVersions\" WHERE \"PublishFrom\" IS NOT NULL;");
        Assert.True(publishedCount >= 13,
            $"Expected every sample to have a published version; got {publishedCount}.");

        // Retired wiki tables must not exist in the current schema — the DropWikiPagePlumbing
        // migration removed them. Query information_schema.tables so the assertion works
        // whether the tables are absent (correct) or present (would flag a schema regression).
        var wikiPagesTableExists = await ScalarLongAsync(conn, @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('WikiPages', 'WikiPageVersions', 'wiki_pages', 'wiki_page_versions');");
        Assert.Equal(0L, wikiPagesTableExists);
    }

    // Pressing the seed button twice must not duplicate pages or overwrite what is already there.
    // The old seeder got this from a path-existence check; the replacement gets it from the
    // importer's Skip mode, so the guarantee is worth re-pinning against a real database.
    [Fact]
    public async Task SeedAsync_IsIdempotent_AndLeavesExistingContentAlone()
    {
        await ResetAsync();

        await using (var ctx = _fixture.CreateContext())
        {
            var pageRepo = new PageNodeRepository(ctx);
            await new DefaultPageNodeSeeder(new PageNodeService(pageRepo), pageRepo).SeedAsync();
        }

        await using (var ctx = _fixture.CreateContext())
        {
            await new SamplePageNodeSeeder(BuildStaging(ctx)).SeedAsync();
        }

        long afterFirst;
        await using (var conn0 = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await conn0.OpenAsync();
            afterFirst = await ScalarLongAsync(conn0, "SELECT COUNT(*) FROM \"PageNodes\";");
        }

        int createdOnSecondRun;
        await using (var ctx = _fixture.CreateContext())
        {
            createdOnSecondRun = await new SamplePageNodeSeeder(BuildStaging(ctx)).SeedAsync();
        }

        Assert.Equal(0, createdOnSecondRun);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var afterSecond = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM \"PageNodes\";");
        Assert.Equal(afterFirst, afterSecond);
    }

    private static ContentStagingService BuildStaging(DfE.CheckPerformanceData.Persistence.Contexts.IPortalDbContext ctx) =>
        new(new PageNodeRepository(ctx), new ContentBlockRepository(ctx), new HtmlRenderingService());

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
