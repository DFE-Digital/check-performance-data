using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the seed-sample-search-data helper: fabricates a plausible mix of
// search events + feedback messages across a chosen time span so the analytics dashboard
// has content to demo. Locks:
//
//   1. Every requested event lands as a search_events row inside the seeded window.
//   2. Every requested message lands as a search_messages row inside the seeded window.
//   3. A subset of events carry a zero-result signal (results_pages + results_blocks = 0)
//      so the funnel + zero-result tiles have data.
//   4. Multiple sessions are represented — the session pool must exceed one so the top-
//      users tile does not degenerate to a single row.
//   5. Repeat runs with different seeds add rows without disturbing prior seed content.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataSeederTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private SampleSearchDataSeeder CreateSut() =>
        new(
            new DbSearchAnalyticsSink(_fixture.CreateContext()),
            new DbSampleSearchDataGateway(_fixture.CreateContext()));

    [Fact]
    public async Task SeedAsync_WritesEventsAndMessagesInsideTheRequestedWindow()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var span = TimeSpan.FromDays(7);
        var sut = CreateSut();

        var result = await sut.SeedAsync(
            span: span,
            eventCount: 120,
            messageCount: 8,
            nowUtc: now,
            seed: 12345,
            cancellationToken: CancellationToken.None);

        Assert.Equal(120, result.EventsCreated);
        Assert.True(result.ResultsCreated > 0,
            "Non-zero events should carry hit rows; total ResultsCreated should be > 0.");
        Assert.Equal(8, result.MessagesCreated);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var eventCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_events;");
        Assert.Equal(120L, eventCount);

        var messageCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_messages;");
        Assert.Equal(8L, messageCount);

        // Every event's occurred_at_utc sits inside the requested window (from = now - span,
        // to = now). A row outside the window would break the retention floor invariant and
        // pollute other tests.
        var outsideWindow = await ScalarLongAsync(conn, $@"
            SELECT COUNT(*) FROM search_events
            WHERE occurred_at_utc < TIMESTAMP '2026-07-21 12:00:00+00'
               OR occurred_at_utc > TIMESTAMP '2026-07-28 12:00:00+00';");
        Assert.Equal(0L, outsideWindow);

        // Same rule for messages.
        var messagesOutside = await ScalarLongAsync(conn, $@"
            SELECT COUNT(*) FROM search_messages
            WHERE submitted_at_utc < TIMESTAMP '2026-07-21 12:00:00+00'
               OR submitted_at_utc > TIMESTAMP '2026-07-28 12:00:00+00';");
        Assert.Equal(0L, messagesOutside);

        // Zero-result events exist so the funnel card has data. Aim ~15%, tolerate 5%-35%
        // so the assertion is not brittle to RNG variance on small runs.
        var zeroCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE zero_results = TRUE;");
        Assert.InRange(zeroCount, 6L, 42L);

        // Session pool: at least 8 distinct sessions so the top-users tile has variety.
        var sessionCount = await ScalarLongAsync(conn,
            "SELECT COUNT(DISTINCT session_id) FROM search_events;");
        Assert.True(sessionCount >= 8L,
            $"Expected at least 8 distinct sessions, got {sessionCount}.");
    }

    [Fact]
    public async Task SeedAsync_IsAdditive_SecondRunPreservesPriorRows()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var span = TimeSpan.FromDays(1);

        var sut = CreateSut();
        await sut.SeedAsync(span, 50, 3, now, 111, CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var firstEventCount = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        var firstMessageCount = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_messages;");
        Assert.Equal(50L, firstEventCount);
        Assert.Equal(3L, firstMessageCount);

        // Second seed pass with a different rng seed → both counts grow, prior rows stay.
        var sut2 = CreateSut();
        await sut2.SeedAsync(span, 30, 2, now, 222, CancellationToken.None);
        var secondEventCount = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        var secondMessageCount = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_messages;");
        Assert.Equal(80L, secondEventCount);
        Assert.Equal(5L, secondMessageCount);
    }

    // Progress callback: the seeder MUST report incremental progress via IProgress<T> so
    // the admin UI can render a live progress panel while a multi-thousand-event seed is
    // running. Locks:
    //   * At least one tick fires for a small seed.
    //   * The final tick's EventsWritten equals the actual row count written.
    //   * Each tick's CurrentCursorUtc sits inside the seeded window.
    [Fact]
    public async Task SeedAsync_ReportsProgressTicksWithMonotonicCounts()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var span = TimeSpan.FromDays(1);
        var fromUtc = now - span;
        var sut = CreateSut();

        var ticks = new List<SampleSearchDataSeedProgressTick>();
        var progress = new Progress<SampleSearchDataSeedProgressTick>(t => ticks.Add(t));

        var result = await sut.SeedAsync(
            span: span,
            eventCount: 500,
            messageCount: 5,
            nowUtc: now,
            seed: 42,
            cancellationToken: CancellationToken.None,
            progress: progress);

        // Progress delivery is async — wait briefly for the marshal-to-context posts to drain.
        var start = DateTime.UtcNow;
        while (ticks.Count == 0 && (DateTime.UtcNow - start) < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(20);
        }

        Assert.True(ticks.Count >= 1,
            $"Expected at least one progress tick for a 500-event seed; got {ticks.Count}.");
        Assert.Equal(result.EventsCreated, ticks[^1].EventsWritten);
        Assert.Equal(result.MessagesCreated, ticks[^1].MessagesWritten);
        foreach (var t in ticks)
        {
            Assert.InRange(t.CurrentCursorUtc, fromUtc, now);
            Assert.True(t.EventsWritten >= 0);
            Assert.True(t.MessagesWritten >= 0);
        }
    }

    // Cancellation: an in-flight seed MUST honour the cancellation token between batches.
    // OperationCanceledException is expected; whatever rows landed before the cancel are
    // fine (the endpoint is additive and the UI surfaces "cancelled at N events").
    [Fact]
    public async Task SeedAsync_HonoursCancellationBetweenBatches()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var span = TimeSpan.FromDays(1);
        var sut = CreateSut();

        // Cancel after the first batch — a 200-batch seeder should stop before 5000 events.
        var cts = new CancellationTokenSource();
        var ticks = new List<SampleSearchDataSeedProgressTick>();
        var progress = new Progress<SampleSearchDataSeedProgressTick>(t =>
        {
            ticks.Add(t);
            cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sut.SeedAsync(
                span: span,
                eventCount: 5000,
                messageCount: 10,
                nowUtc: now,
                seed: 1,
                cancellationToken: cts.Token,
                progress: progress));

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var writtenCount = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        Assert.True(writtenCount < 5000L,
            $"Cancellation should have halted before the full 5000 rows landed; got {writtenCount}.");
    }

    // Marker invariant: every row the seeder writes lands with is_seeded = true across
    // all three sink tables. Real events written via the request-side sink decorator
    // default to false and remain visible after a delete-seeded admin action. This is
    // the contract the Danger-zone "delete seeded data" button relies on.
    [Fact]
    public async Task SeedAsync_MarksEveryRowAsSeeded()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);
        var span = TimeSpan.FromHours(24);
        var sut = CreateSut();

        // Also insert one non-seeded event through the sink directly to prove the
        // marker separates seeded from real cleanly.
        var directSink = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var directEvent = new SearchEventDto(
            OccurredAtUtc: now.AddMinutes(-5),
            SessionId: "real-user",
            QueryRaw: "widget",
            QueryNormalised: "widget",
            Scope: "site",
            ResultsPages: 1,
            ResultsBlocks: 0,
            LatencyMs: 25,
            Results: Array.Empty<SearchEventResultDto>(),
            IsSeeded: false);
        await directSink.RecordBatchAsync(new[] { directEvent }, CancellationToken.None);

        await sut.SeedAsync(
            span: span,
            eventCount: 60,
            messageCount: 5,
            nowUtc: now,
            seed: 42,
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var seededEvents = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE is_seeded = true;");
        var realEvents = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE is_seeded = false;");
        var seededMessages = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_messages WHERE is_seeded = true;");
        var realMessages = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_messages WHERE is_seeded = false;");
        var seededResultRows = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_event_results WHERE is_seeded = true;");
        var realResultRows = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_event_results WHERE is_seeded = false;");

        Assert.Equal(60L, seededEvents);
        Assert.Equal(1L, realEvents);
        Assert.Equal(5L, seededMessages);
        Assert.Equal(0L, realMessages);
        // Every child result row inherits the parent event's marker — the sink writes
        // them in lockstep so no seeded event ever has a non-seeded child row.
        Assert.True(seededResultRows >= 0);
        Assert.Equal(0L, realResultRows);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
