using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// The fixture seeder exists because a page can be emptied without being deleted: an editor removes
// the version, the row survives, and the route 404s from then on. The sample seeder cannot repair
// that — it skips on collision, and the collision test is the page identity, not whether the page
// has anything to render. Every claim about the repair is about what the importer does to real
// rows, so it is only worth making against a real database.
[Collection(nameof(PostgresCollection))]
public sealed class TestFixturePageNodeSeederTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"" RESTART IDENTITY CASCADE;");
    }

    private async Task<int> SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new PageNodeRepository(ctx);
        return await new TestFixturePageNodeSeeder(
            new PageNodeService(repo), repo, BuildStaging(ctx)).SeedAsync();
    }

    [Fact]
    public async Task SeedAsync_CreatesTheRootAndEveryFixtureBeneathIt()
    {
        await ResetAsync();

        var touched = await SeedAsync();

        var expected = TestFixtureSeedBundle.Load().PageNodes.Count;
        Assert.Equal(expected, touched);

        await using var conn = await OpenAsync();

        // The root is the seeder's own — nothing else creates it — and it has to be a folder that
        // stays out of the menu, or the fixture tree shows up as a site section.
        var root = await SingleRowAsync(conn, @"
            SELECT ""PageType"", ""ShowInMenu""::text, ""AppearInSearch""::text
            FROM ""PageNodes"" WHERE ""Id"" = @id;", DefaultPageNodeRoots.DevelopmentTestingRootId);
        Assert.Equal(["folder", "false", "false"], root);

        // Every fixture lands published under that root, at the path the browser suite navigates to.
        foreach (var page in TestFixtureSeedBundle.Load().PageNodes)
        {
            var path = $"{DefaultPageNodeRoots.DevelopmentTestingSegment}/{page.Segment}";
            var published = await ScalarLongAsync(conn, @"
                SELECT COUNT(*) FROM ""PageNodes"" n
                JOIN ""PageNodeVersions"" v ON v.""PageNodeId"" = n.""Id""
                WHERE n.""Path"" = @p AND n.""ParentId"" = @root AND v.""PublishFrom"" IS NOT NULL;",
                ("p", path), ("root", DefaultPageNodeRoots.DevelopmentTestingRootId));
            Assert.True(published > 0, $"fixture '{path}' has no published version after seeding");
        }
    }

    // The defect this seeder was written for. Deleting a page's versions leaves the row in place,
    // so the sample seeder's Skip-on-collision walks past it and the 404 is permanent. Replace
    // re-imports the versions over the top, which is the only thing that brings the page back.
    [Fact]
    public async Task SeedAsync_RepairsAFixtureWhoseVersionsWereDeleted()
    {
        await ResetAsync();
        await SeedAsync();

        var longPageId = TestFixtureSeedBundle.Load().PageNodes
            .Single(p => p.Segment == TestFixtureSeedBundle.LongPageSegment).Id;

        // Exactly the state an editor leaves behind: the page still exists, it just has nothing
        // to render.
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ""PageNodeVersions"" WHERE ""PageNodeId"" = {0};", longPageId);
        }

        await using (var conn = await OpenAsync())
        {
            var before = await ScalarLongAsync(conn,
                @"SELECT COUNT(*) FROM ""PageNodeVersions"" WHERE ""PageNodeId"" = @id;", ("id", longPageId));
            Assert.Equal(0L, before);

            var rowSurvives = await ScalarLongAsync(conn,
                @"SELECT COUNT(*) FROM ""PageNodes"" WHERE ""Id"" = @id;", ("id", longPageId));
            Assert.Equal(1L, rowSurvives);
        }

        await SeedAsync();

        await using var after = await OpenAsync();
        var restored = await ScalarLongAsync(after, @"
            SELECT COUNT(*) FROM ""PageNodeVersions""
            WHERE ""PageNodeId"" = @id AND ""PublishFrom"" IS NOT NULL;", ("id", longPageId));
        Assert.True(restored > 0, "re-seeding did not restore the emptied fixture — the route stays 404");
    }

    // Pressing the button twice must not build a second copy of the tree. Replace mode updates in
    // place, and the root guard stops a second root appearing beside the first.
    [Fact]
    public async Task SeedAsync_IsIdempotent_AndDoesNotDuplicateTheTree()
    {
        await ResetAsync();
        await SeedAsync();

        long afterFirst;
        await using (var conn = await OpenAsync())
            afterFirst = await ScalarLongAsync(conn, @"SELECT COUNT(*) FROM ""PageNodes"";");

        await SeedAsync();

        await using var conn2 = await OpenAsync();
        var afterSecond = await ScalarLongAsync(conn2, @"SELECT COUNT(*) FROM ""PageNodes"";");
        Assert.Equal(afterFirst, afterSecond);
    }

    // Fixtures and sample content are separate bundles seeded through the same button, one after
    // the other. The fixture import runs in Replace mode, so if the two shared an identity the
    // second pass would silently overwrite a page belonging to the first. Seeding both and
    // counting the rows is what proves they are actually independent trees.
    [Fact]
    public async Task SeedAsync_LeavesTheSampleContentUntouched()
    {
        await ResetAsync();

        await using (var ctx = _fixture.CreateContext())
        {
            var repo = new PageNodeRepository(ctx);
            await new DefaultPageNodeSeeder(new PageNodeService(repo), repo).SeedAsync();
        }
        await using (var ctx = _fixture.CreateContext())
            await new SamplePageNodeSeeder(BuildStaging(ctx)).SeedAsync();

        long samplesBefore;
        await using (var conn = await OpenAsync())
            samplesBefore = await ScalarLongAsync(conn, @"
                SELECT COUNT(*) FROM ""PageNodes""
                WHERE ""ParentId"" IS NOT NULL AND ""ParentId"" <> @root;",
                ("root", DefaultPageNodeRoots.DevelopmentTestingRootId));

        await SeedAsync();

        await using var conn2 = await OpenAsync();
        var samplesAfter = await ScalarLongAsync(conn2, @"
            SELECT COUNT(*) FROM ""PageNodes""
            WHERE ""ParentId"" IS NOT NULL AND ""ParentId"" <> @root;",
            ("root", DefaultPageNodeRoots.DevelopmentTestingRootId));

        Assert.Equal(samplesBefore, samplesAfter);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static ContentStagingService BuildStaging(IPortalDbContext ctx) =>
        new(new PageNodeRepository(ctx), new ContentBlockRepository(ctx), new HtmlRenderingService());

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string[]> SingleRowAsync(NpgsqlConnection conn, string sql, Guid id)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected exactly one row");
        var values = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.GetString(i);
        return values;
    }
}
