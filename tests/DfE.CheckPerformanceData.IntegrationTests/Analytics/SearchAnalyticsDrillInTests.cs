using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Read-side tests for the three admin drill-in tables (top queries, top zero-result queries,
// top pages returned). Each reader returns (Rows, TotalCount) so the view can render both the
// current page and the pagination widget from a single call. Every SQL parameter binds via
// Npgsql — the read path has no injection surface even though the paging inputs (page, pageSize)
// arrive from the query string.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsDrillInTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsDrillInTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- GetPagedTopQueriesAsync: paged + ordered by count DESC + total is DISTINCT count ---

    [Fact]
    public async Task GetPagedTopQueries_FirstPage_ReturnsAllDistinctQueriesOrderedByCountDesc()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        // 3 dominant queries (20 hits each) + 5 zero-result queries (5 hits each) = 85 rows, 8 distinct.
        await SeedDrillInCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        // 8 distinct queries: 3 with results + 5 zero-result. All fit on one page of 10.
        Assert.Equal(8, total);
        Assert.Equal(8, rows.Count);

        // Dominant queries lead by count (20 each); zero-result queries follow (5 each).
        Assert.Equal(20, rows[0].Count);
        Assert.Equal(20, rows[1].Count);
        Assert.Equal(20, rows[2].Count);
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "alpha");
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "bravo");
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "charlie");
    }

    [Fact]
    public async Task GetPagedTopQueries_PageSmallerThanCorpus_PagesCorrectly()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedDrillInCorpusAsync(now);

        var (page1, total1) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 1, pageSize: 3, CancellationToken.None);
        var (page2, total2) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 2, pageSize: 3, CancellationToken.None);
        var (page3, total3) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 3, pageSize: 3, CancellationToken.None);
        var (page4, total4) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 4, pageSize: 3, CancellationToken.None);

        Assert.Equal(8, total1);
        Assert.Equal(8, total2);
        Assert.Equal(8, total3);
        Assert.Equal(8, total4);
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.Equal(2, page3.Count);
        Assert.Empty(page4);

        // No cross-page duplicates.
        var union = page1.Concat(page2).Concat(page3).Select(r => r.QueryNormalised).ToList();
        Assert.Equal(union.Distinct().Count(), union.Count);
    }

    [Fact]
    public async Task GetPagedTopQueries_EmptyWindow_ReturnsEmptyRowsAndZeroTotal()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;

        var (rows, total) = await CreateService()
            .GetPagedTopQueriesAsync(now.AddHours(-1), now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Empty(rows);
        Assert.Equal(0, total);
    }

    // --- GetPagedTopZeroResultQueriesAsync: filters to zero_results = true ---

    [Fact]
    public async Task GetPagedTopZeroResults_ReturnsOnlyZeroResultQueries()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedDrillInCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetPagedTopZeroResultQueriesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        // 5 distinct zero-result queries in the corpus.
        Assert.Equal(5, total);
        Assert.Equal(5, rows.Count);
        Assert.DoesNotContain(rows, r => r.QueryNormalised is "alpha" or "bravo" or "charlie");
        Assert.All(rows, r => Assert.StartsWith("gap-", r.QueryNormalised));
        Assert.All(rows, r => Assert.Equal(5, r.Count));
    }

    // --- GetTopPagesAsync: joins search_event_results to search_events, groups by result_key ---

    [Fact]
    public async Task GetTopPages_ReturnsPagesInImpressionOrder()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        // 10 distinct pages, each impressed 6 times = 60 total impressions.
        await SeedTopPagesCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetTopPagesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(10, total);
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.Equal(6, r.ImpressionCount));
        // Rows are ordered by ImpressionCount DESC; ties break deterministically so the same query
        // returns the same order twice — ordered by result_key ascending as the tie-break.
        var byKeyAscending = rows.OrderBy(r => r.ResultKey).Select(r => r.ResultKey).ToList();
        Assert.Equal(byKeyAscending, rows.Select(r => r.ResultKey).ToList());
    }

    [Fact]
    public async Task GetTopPages_HonoursPageAndPageSize()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedTopPagesCorpusAsync(now);

        var (page1, total1) = await CreateService()
            .GetTopPagesAsync(from, now, page: 1, pageSize: 4, CancellationToken.None);
        var (page2, total2) = await CreateService()
            .GetTopPagesAsync(from, now, page: 2, pageSize: 4, CancellationToken.None);
        var (page3, total3) = await CreateService()
            .GetTopPagesAsync(from, now, page: 3, pageSize: 4, CancellationToken.None);

        Assert.Equal(10, total1);
        Assert.Equal(10, total2);
        Assert.Equal(10, total3);
        Assert.Equal(4, page1.Count);
        Assert.Equal(4, page2.Count);
        Assert.Equal(2, page3.Count);
    }

    [Fact]
    public async Task GetTopPages_ExcludesResultsOutsideTheWindow()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;

        // One event with a hit inside the window, one older event with a different hit outside.
        var insideEvent = NewEvent(now.AddMinutes(-30), "s-in", "query-in", results: 1, latency: 10);
        var outsideEvent = NewEvent(now.AddDays(-30), "s-out", "query-out", results: 1, latency: 10);

        long insideId, outsideId;
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.Add(insideEvent);
            ctx.SearchEvents.Add(outsideEvent);
            await ctx.SaveChangesAsync();
            insideId = insideEvent.Id;
            outsideId = outsideEvent.Id;

            ctx.SearchEventResults.Add(NewResult(insideId, "https://example.gov.uk/inside", position: 1));
            ctx.SearchEventResults.Add(NewResult(outsideId, "https://example.gov.uk/outside", position: 1));
            await ctx.SaveChangesAsync();
        }

        var (rows, total) = await CreateService()
            .GetTopPagesAsync(now.AddHours(-1), now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(rows);
        Assert.Equal("https://example.gov.uk/inside", rows[0].ResultKey);
    }

    // --- Corpus builders ---

    private async Task SeedDrillInCorpusAsync(DateTime now)
    {
        var events = new List<SearchEvent>();
        foreach (var q in new[] { "alpha", "bravo", "charlie" })
        {
            for (var i = 0; i < 20; i++)
            {
                // Space rows across the last hour so they all land inside the [now-1d, now) window.
                events.Add(NewEvent(now.AddMinutes(-i * 2), $"s-{q}-{i}", q, results: 3, latency: 15));
            }
        }
        for (var g = 1; g <= 5; g++)
        {
            var name = $"gap-{g}";
            for (var i = 0; i < 5; i++)
            {
                events.Add(NewEvent(now.AddMinutes(-i * 3 - 10), $"s-{name}-{i}", name, results: 0, latency: 12));
            }
        }
        await using var ctx = _fixture.CreateContext();
        ctx.SearchEvents.AddRange(events);
        await ctx.SaveChangesAsync();
    }

    // 10 pages, 6 impressions each = 60 total impressions attached to 10 parent search_events.
    // Each parent event holds 6 result rows, one per page — mirrors a real result set landing
    // 10 hits for a single query.
    private async Task SeedTopPagesCorpusAsync(DateTime now)
    {
        var events = Enumerable.Range(0, 6)
            .Select(i => NewEvent(now.AddMinutes(-i * 4 - 5), $"s-page-{i}", $"query-{i}", results: 10, latency: 15))
            .ToList();

        long[] eventIds;
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.AddRange(events);
            await ctx.SaveChangesAsync();
            eventIds = events.Select(e => e.Id).ToArray();
        }

        await using (var ctx = _fixture.CreateContext())
        {
            for (var i = 0; i < 6; i++)
            {
                for (var p = 0; p < 10; p++)
                {
                    ctx.SearchEventResults.Add(NewResult(eventIds[i], $"https://example.gov.uk/page-{p:00}", position: p + 1));
                }
            }
            await ctx.SaveChangesAsync();
        }
    }

    // --- Helpers ---

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(_fixture.CreateContext());

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc,
        string sessionId,
        string queryNormalised,
        int results,
        int latency) => new()
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

    private static SearchEventResult NewResult(long searchEventId, string resultKey, int position) => new()
    {
        SearchEventId = searchEventId,
        Position = position,
        ResultKind = "page",
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
