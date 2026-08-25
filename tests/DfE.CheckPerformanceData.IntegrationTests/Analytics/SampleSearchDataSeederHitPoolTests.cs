using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Hit-pool contract for SampleSearchDataSeeder. The seeder's non-zero events emit
// search_event_results rows whose result_key MUST come from real CMS state — real pages
// (kind = "page", key = "/path") + real blocks (kind = "block", key = block-key). No
// fictional keys must leak in: that regression was the whole point of moving from the old
// hardcoded string[] pool to the injected repositories.
//
// Two tests:
//   * Wired dependencies: seeded result_key values ⊆ union of page paths + block keys.
//   * Null dependencies (backward-compat): seeder still runs and emits results, using the
//     internal sentinel fallback.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataSeederHitPoolTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataSeederHitPoolTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedAsync_WithWiredPagesAndBlocks_EveryResultKeyComesFromTheCmsPool()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var pageKeys = new[]
        {
            "/help/pupil-premium",
            "/guidance/amendment-window",
            "/help/school-census",
        };
        var blockKeys = new[]
        {
            "block-nav-admin",
            "block-hero-welcome",
        };

        // Fake IPageNodeRepository — only GetTreeAsync is exercised by the seeder. The
        // rest of the interface is unused, so NSubstitute defaults are fine.
        var pageRepo = Substitute.For<IPageNodeRepository>();
        pageRepo.GetTreeAsync().Returns(pageKeys
            .Select(p => new PageNodeTreeItemDto
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                Segment = p.TrimStart('/'),
                Path = p.TrimStart('/'),
                Title = p.TrimStart('/'),
                PageType = "content",
                HasLiveVersion = true,
            })
            .ToList());

        var blockService = Substitute.For<IContentBlockService>();
        blockService.GetAllAsync().Returns(blockKeys
            .Select(k => new ContentBlockDto { Key = k, BlockType = "text", Value = "sample" })
            .ToList());

        var seeder = new SampleSearchDataSeeder(
            sink: new DbSearchAnalyticsSink(_fixture.CreateContext()),
            messagesGateway: new DbSampleSearchDataGateway(_fixture.CreateContext()),
            pageRepository: pageRepo,
            contentBlockService: blockService);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var result = await seeder.SeedAsync(
            span: TimeSpan.FromDays(7),
            eventCount: 60,
            messageCount: 0,
            nowUtc: now,
            seed: 424242,
            cancellationToken: CancellationToken.None);

        Assert.True(result.ResultsCreated > 0);

        // The union of expected keys the seeder is allowed to draw from — pages carry a
        // "/" prefix (the seeder's normalisation, see BuildHitPoolAsync) and blocks are
        // raw block keys. Anything outside this set is a fictional key leak.
        var expected = pageKeys
            .Select(p => "/" + p.TrimStart('/'))
            .Concat(blockKeys)
            .ToHashSet(StringComparer.Ordinal);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT result_key FROM search_event_results;";
        await using var reader = await cmd.ExecuteReaderAsync();

        var seen = new List<string>();
        while (await reader.ReadAsync())
        {
            seen.Add(reader.GetString(0));
        }

        Assert.NotEmpty(seen);
        var leaked = seen.Where(k => !expected.Contains(k)).ToList();
        Assert.True(leaked.Count == 0,
            $"Seeder wrote result_key values not in the wired CMS pool: [{string.Join(", ", leaked)}]");
    }

    [Fact]
    public async Task SeedAsync_With500PlusPagesAndBlocks_StillEmitsBothKinds()
    {
        // Regression: BuildHitPoolAsync used to do pages.Concat(blocks).Take(500), so a
        // corpus of >=500 live pages silently dropped every block. Verify the fix keeps
        // both kinds present in the seeded rows.
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var pageKeys = Enumerable.Range(0, 800)
            .Select(i => $"/help/page-{i:0000}")
            .ToArray();
        var blockKeys = new[] { "block-nav", "block-hero", "block-footer" };

        var pageRepo = Substitute.For<IPageNodeRepository>();
        pageRepo.GetTreeAsync().Returns(pageKeys
            .Select(p => new PageNodeTreeItemDto
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                Segment = p.TrimStart('/'),
                Path = p.TrimStart('/'),
                Title = p.TrimStart('/'),
                PageType = "content",
                HasLiveVersion = true,
            })
            .ToList());

        var blockService = Substitute.For<IContentBlockService>();
        blockService.GetAllAsync().Returns(blockKeys
            .Select(k => new ContentBlockDto { Key = k, BlockType = "text", Value = "sample" })
            .ToList());

        var seeder = new SampleSearchDataSeeder(
            sink: new DbSearchAnalyticsSink(_fixture.CreateContext()),
            messagesGateway: new DbSampleSearchDataGateway(_fixture.CreateContext()),
            pageRepository: pageRepo,
            contentBlockService: blockService);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var result = await seeder.SeedAsync(
            span: TimeSpan.FromDays(7),
            eventCount: 200,
            messageCount: 0,
            nowUtc: now,
            seed: 7777,
            cancellationToken: CancellationToken.None);

        Assert.True(result.ResultsCreated > 0);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT result_kind, COUNT(*) FROM search_event_results GROUP BY result_kind;";
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        Assert.True(counts.TryGetValue("page", out var pageCount) && pageCount > 0,
            "Expected at least one page-kind result row.");
        Assert.True(counts.TryGetValue("block", out var blockCount) && blockCount > 0,
            "Expected at least one block-kind result row even with 800 live pages in the pool.");
    }

    [Fact]
    public async Task SeedAsync_WithBothCmsDependenciesNull_StillRunsUsingSentinelFallback()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var seeder = new SampleSearchDataSeeder(
            sink: new DbSearchAnalyticsSink(_fixture.CreateContext()),
            messagesGateway: new DbSampleSearchDataGateway(_fixture.CreateContext()),
            pageRepository: null,
            contentBlockService: null);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var result = await seeder.SeedAsync(
            span: TimeSpan.FromDays(3),
            eventCount: 40,
            messageCount: 0,
            nowUtc: now,
            seed: 999,
            cancellationToken: CancellationToken.None);

        Assert.Equal(40, result.EventsCreated);

        // With no wired CMS deps the seeder falls back to the internal sentinel pool. That
        // pool MUST be big enough to satisfy the 3-25 hits-per-event invariant PickHits
        // promises: seenKeys dedup + a single-entry pool would collapse every event to one
        // hit and flatten Top Pages / Top Content Blocks / long-tail histograms to a
        // single row.
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(DISTINCT result_key) FROM search_event_results;";
        var distinct = (long)(await cmd.ExecuteScalarAsync())!;
        // 40 events with variable hit counts; the sentinel pool has 30 distinct entries and
        // the seed is deterministic — expect the run to touch a broad slice of the pool.
        Assert.True(distinct >= 15,
            $"Expected the sentinel pool to expose >=15 distinct result_key values on a 40-event run; got {distinct}.");

        // Both kinds must be represented so the block-side dashboards are non-empty on a
        // demo-on-empty-db seed.
        await using var kindCmd = conn.CreateCommand();
        kindCmd.CommandText = @"
            SELECT COUNT(DISTINCT result_kind) FROM search_event_results;";
        var kinds = (long)(await kindCmd.ExecuteScalarAsync())!;
        Assert.Equal(2, kinds);
    }
}
