using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// GetTopBlocksAsync is the sibling of GetTopPagesAsync: same shared SQL underneath, but
// filtered to result_kind = 'block' so the "Top content blocks" card renders block keys
// only. Splitting the two matters because pages and blocks live in the same table but are
// semantically different — a page is a URL, a block is a snippet — and mixing them on one
// card produces a "Top pages" list with non-navigable entries mid-list.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsTopBlocksTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsTopBlocksTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetTopBlocks_AggregatesOnlyBlockKindRows()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        var parent = NewEvent(now.AddMinutes(-30), "s-mixed", "q", results: 5, latency: 10);
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.Add(parent);
            await ctx.SaveChangesAsync();

            // Two block hits + one page hit hang off the same parent event. The reader
            // must return only the two block rows, and the page row must NOT bleed in.
            ctx.SearchEventResults.Add(NewResult(parent.Id, "block-a", "block", position: 1));
            ctx.SearchEventResults.Add(NewResult(parent.Id, "block-b", "block", position: 2));
            ctx.SearchEventResults.Add(NewResult(parent.Id, "https://example.gov.uk/page", "page", position: 3));
            await ctx.SaveChangesAsync();
        }

        var (rows, total) = await CreateService()
            .GetTopBlocksAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.StartsWith("block-", r.ResultKey));
    }

    [Fact]
    public async Task GetTopBlocks_And_GetTopPages_ProduceNonOverlappingKeys_ForMixedCorpus()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddHours(-6);

        // Same-event corpus with three block hits + three page hits. Neither reader may
        // return the other's keys — the split on result_kind is the whole point of the
        // "Top content blocks" card existing.
        var parent = NewEvent(now.AddMinutes(-30), "s-split", "q", results: 6, latency: 10);
        var blockKeys = new[] { "block-header", "block-footer", "block-cta" };
        var pageKeys = new[]
        {
            "https://example.gov.uk/one",
            "https://example.gov.uk/two",
            "https://example.gov.uk/three",
        };
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.Add(parent);
            await ctx.SaveChangesAsync();

            var position = 1;
            foreach (var k in blockKeys)
                ctx.SearchEventResults.Add(NewResult(parent.Id, k, "block", position: position++));
            foreach (var k in pageKeys)
                ctx.SearchEventResults.Add(NewResult(parent.Id, k, "page", position: position++));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (blocks, blockTotal) = await svc.GetTopBlocksAsync(from, now, page: 1, pageSize: 20, CancellationToken.None);
        var (pages,  pageTotal)  = await svc.GetTopPagesAsync (from, now, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(3, blockTotal);
        Assert.Equal(3, pageTotal);

        var blockKeySet = blocks.Select(r => r.ResultKey).ToHashSet();
        var pageKeySet  = pages .Select(r => r.ResultKey).ToHashSet();
        Assert.Equal(blockKeys.ToHashSet(), blockKeySet);
        Assert.Equal(pageKeys.ToHashSet(),  pageKeySet);
        Assert.Empty(blockKeySet.Intersect(pageKeySet));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(_fixture.CreateContext());

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc, string sessionId, string queryNormalised,
        int results, int latency) => new()
        {
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            SessionId = sessionId,
            QueryRaw = queryNormalised,
            QueryNormalised = queryNormalised,
            Scope = null,
            ResultsPages = results,
            ResultsBlocks = 0,
            LatencyMs = latency,
        };

    private static SearchEventResult NewResult(long searchEventId, string resultKey, string kind, int position) => new()
    {
        SearchEventId = searchEventId,
        Position = position,
        ResultKind = kind,
        ResultKey = resultKey,
        Rank = 1.0f,
    };

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
    }
}
