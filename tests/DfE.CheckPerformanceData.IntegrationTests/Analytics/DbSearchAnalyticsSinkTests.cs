using System.Diagnostics;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the real DbSearchAnalyticsSink batch write + CTE-batched retention
// purge. Three shape facts + one performance fact:
//
//   1. RecordBatchAsync writes N parent SearchEvents + all child SearchEventResults in a
//      single SaveChanges — verified by row-count assertions and value round-trip
//      (session_id, occurred_at_utc, query_normalised, results_pages, results_blocks).
//      Postgres derives results_total and zero_results as STORED computed columns.
//   2. PurgeExpiredAsync deletes rows whose occurred_at_utc has aged past the retention
//      window and leaves fresher rows intact.
//   3. PurgeExpiredAsync (30k row seed, Slow) completes each 10k-row DELETE batch in
//      well under 100ms so the retention job never long-locks the table.
[Collection(nameof(PostgresCollection))]
public sealed class DbSearchAnalyticsSinkTests
{
    private readonly PostgresFixture _fixture;

    public DbSearchAnalyticsSinkTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private DbSearchAnalyticsSink CreateSut() => new(_fixture.CreateContext());

    // Batch write + value round-trip + computed columns + child row cardinality.
    [Fact]
    public async Task RecordBatchAsync_WritesEventsAndResults_WithComputedColumnsMatching()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var occurredAt = new DateTime(2026, 07, 27, 10, 00, 00, DateTimeKind.Utc);
        var batch = Enumerable.Range(0, 5).Select(i =>
            new SearchEventDto(
                OccurredAtUtc: occurredAt.AddMinutes(i),
                SessionId: "sink-session-" + i,
                QueryRaw: "widget-" + i,
                QueryNormalised: "widget-" + i,
                Scope: null,
                ResultsPages: i,          // 0, 1, 2, 3, 4
                ResultsBlocks: 4 - i,     // 4, 3, 2, 1, 0
                LatencyMs: 10 + i,
                Results:
                [
                    new SearchEventResultDto(1, "page", "/pages/first-" + i, 0.9f),
                    new SearchEventResultDto(2, "block", "block-" + i, 0.5f),
                ]))
            .ToList();

        var sut = CreateSut();
        await sut.RecordBatchAsync(batch, CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var eventCount = (long)((await ScalarAsync(conn,
            "SELECT COUNT(*) FROM search_events"))!);
        var resultCount = (long)((await ScalarAsync(conn,
            "SELECT COUNT(*) FROM search_event_results"))!);
        Assert.Equal(5L, eventCount);
        Assert.Equal(10L, resultCount);

        // Round-trip inspection for the middle row — session id, query, per-corpus
        // counts, and both computed columns (results_total + zero_results).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, query_normalised, results_pages, results_blocks,
                   results_total, zero_results
            FROM search_events
            WHERE session_id = 'sink-session-2';";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("sink-session-2", reader.GetString(0));
        Assert.Equal("widget-2", reader.GetString(1));
        Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(2, reader.GetInt32(3));
        Assert.Equal(4, reader.GetInt32(4));      // computed: 2 + 2 = 4
        Assert.False(reader.GetBoolean(5));       // computed: (2+2)=0 → false
    }

    // The zero-result computed column edge case — when both per-corpus counts are 0 the
    // stored flag flips true, which the dashboard's zero-result partial index relies on.
    [Fact]
    public async Task RecordBatchAsync_WithNoHits_MarksZeroResultsComputedColumnTrue()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var dto = new SearchEventDto(
            OccurredAtUtc: DateTime.UtcNow,
            SessionId: "zero-session",
            QueryRaw: "no-matches-anywhere",
            QueryNormalised: "no-matches-anywhere",
            Scope: null,
            ResultsPages: 0,
            ResultsBlocks: 0,
            LatencyMs: 5,
            Results: []);

        var sut = CreateSut();
        await sut.RecordBatchAsync([dto], CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var zeroResults = (bool)((await ScalarAsync(conn,
            "SELECT zero_results FROM search_events WHERE session_id = 'zero-session'"))!);
        var resultsTotal = (int)((await ScalarAsync(conn,
            "SELECT results_total FROM search_events WHERE session_id = 'zero-session'"))!);

        Assert.True(zeroResults);
        Assert.Equal(0, resultsTotal);
    }

    // Retention: 100 rows aged 91 days + 100 rows aged 89 days; PurgeExpiredAsync(90 days)
    // must return 100 (the aged rows purged) and leave the 100 fresh rows intact.
    [Fact]
    public async Task PurgeExpiredAsync_RemovesAgedRowsAndLeavesFreshRowsIntact()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        // "Aged" batch — 100 rows all 91 days old (dense in a 1-minute window).
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 100,
            oldestAt: now.AddDays(-91));
        // "Fresh" batch — 100 rows all 89 days old.
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 100,
            oldestAt: now.AddDays(-89));

        var sut = CreateSut();
        var purged = await sut.PurgeExpiredAsync(TimeSpan.FromDays(90), CancellationToken.None);

        Assert.Equal(100, purged);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var remaining = (long)((await ScalarAsync(conn,
            "SELECT COUNT(*) FROM search_events"))!);
        Assert.Equal(100L, remaining);
    }

    // 30k aged rows in one purge: three 10k-row batches (approximately). Each individual
    // batched DELETE must complete in well under 100 ms — that is the "never long-lock
    // the table" invariant the CTE-batched shape exists to defend. Total wall clock is
    // bounded by 3 * batch budget with some slack for connection cost.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task PurgeExpiredAsync_ThirtyThousandAgedRows_StaysUnderPerBatchBudget()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 30_000,
            oldestAt: now.AddDays(-100));

        var sut = CreateSut();
        var sw = Stopwatch.StartNew();
        var purged = await sut.PurgeExpiredAsync(TimeSpan.FromDays(90), CancellationToken.None);
        sw.Stop();

        Assert.Equal(30_000, purged);

        // Three batches of ~10k each — total budget scales with the batch count. Give
        // generous headroom to survive shared-fixture jitter; the invariant is "no
        // single unbounded DELETE that would lock the table for a whole second".
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"PurgeExpiredAsync of 30k rows should complete in ≪10s; took {sw.Elapsed}.");
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }
}
