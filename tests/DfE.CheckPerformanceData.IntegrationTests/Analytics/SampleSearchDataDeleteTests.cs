using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Locks the Danger-zone delete contract on the sample-search-data gateway. Two flavours:
//   * DeleteSeeded — WHERE is_seeded = true across the three tables. Real (is_seeded =
//     false) rows survive so a support-team member can still see the messages a real
//     user submitted after an admin wipes the demo data.
//   * DeleteAll — every row regardless of marker. Used by the typed-confirmation
//     "delete all data" action to return the sink to a genuinely empty state.
// Both must run in a single transaction so a partial failure cannot leave the parent
// and child rows in an inconsistent shape.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataDeleteTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataDeleteTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private (SampleSearchDataSeeder Seeder, DbSampleSearchDataGateway Gateway) CreateSut()
    {
        var seeder = new SampleSearchDataSeeder(
            new DbSearchAnalyticsSink(_fixture.CreateContext()),
            new DbSampleSearchDataGateway(_fixture.CreateContext()));
        var gateway = new DbSampleSearchDataGateway(_fixture.CreateContext());
        return (seeder, gateway);
    }

    private async Task<(long Events, long Results, long Messages)> CountsAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var events = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        var results = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_event_results;");
        var messages = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_messages;");
        return (events, results, messages);
    }

    private async Task InsertRealEventAsync(string sessionId, DateTime occurredAt)
    {
        var sink = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var dto = new SearchEventDto(
            OccurredAtUtc: occurredAt,
            SessionId: sessionId,
            QueryRaw: "widget",
            QueryNormalised: "widget",
            Scope: "site",
            ResultsPages: 1,
            ResultsBlocks: 0,
            LatencyMs: 25,
            Results: new[]
            {
                new SearchEventResultDto(1, "page", "/help/widget", 0.9f),
            },
            IsSeeded: false);
        await sink.RecordBatchAsync(new[] { dto }, CancellationToken.None);
    }

    // Delete-seeded preserves real (is_seeded = false) rows and drops every seeded row.
    // Counts returned by the gateway match what actually left the tables.
    [Fact]
    public async Task DeleteSeededAsync_RemovesOnlySeededRows_PreservingRealData()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var (seeder, gateway) = CreateSut();

        // Real event first — this must survive the delete-seeded action.
        await InsertRealEventAsync("real-user-session", now.AddMinutes(-10));

        // Then seed 30 events / 3 messages via the sample seeder — all is_seeded = true.
        await seeder.SeedAsync(
            span: TimeSpan.FromHours(24),
            eventCount: 30,
            messageCount: 3,
            nowUtc: now,
            seed: 99,
            cancellationToken: CancellationToken.None);

        var before = await CountsAsync();
        Assert.Equal(31L, before.Events);       // 30 seeded + 1 real
        Assert.True(before.Results >= 1L);      // at least the one real child row

        var result = await gateway.DeleteSeededAsync(CancellationToken.None);

        var after = await CountsAsync();
        Assert.Equal(1L, after.Events);         // only the real row survives
        Assert.Equal(1L, after.Results);        // real child row survives (1 result)
        Assert.Equal(0L, after.Messages);       // all messages were seeded

        Assert.Equal(30, result.EventsDeleted);
        Assert.Equal(3, result.MessagesDeleted);
        Assert.True(result.ResultsDeleted >= 0);
    }

    // Delete-all wipes every row regardless of marker — including the real event.
    [Fact]
    public async Task DeleteAllAsync_RemovesEveryRow_RegardlessOfMarker()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var (seeder, gateway) = CreateSut();

        await InsertRealEventAsync("real-user-session-2", now.AddMinutes(-10));
        await seeder.SeedAsync(
            span: TimeSpan.FromHours(24),
            eventCount: 20,
            messageCount: 2,
            nowUtc: now,
            seed: 100,
            cancellationToken: CancellationToken.None);

        var before = await CountsAsync();
        Assert.Equal(21L, before.Events);

        var result = await gateway.DeleteAllAsync(CancellationToken.None);

        var after = await CountsAsync();
        Assert.Equal(0L, after.Events);
        Assert.Equal(0L, after.Results);
        Assert.Equal(0L, after.Messages);

        Assert.Equal(21, result.EventsDeleted);
        Assert.Equal(2, result.MessagesDeleted);
    }

    // Empty-sink delete is a no-op that returns zero counts — the endpoint must not
    // 500 if an admin double-clicks the button after a successful delete.
    [Fact]
    public async Task DeleteSeededAsync_OnEmptyTables_ReturnsZeroCountsWithoutThrowing()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var (_, gateway) = CreateSut();

        var result = await gateway.DeleteSeededAsync(CancellationToken.None);
        Assert.Equal(0, result.EventsDeleted);
        Assert.Equal(0, result.ResultsDeleted);
        Assert.Equal(0, result.MessagesDeleted);
    }

    [Fact]
    public async Task DeleteAllAsync_OnEmptyTables_ReturnsZeroCountsWithoutThrowing()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var (_, gateway) = CreateSut();

        var result = await gateway.DeleteAllAsync(CancellationToken.None);
        Assert.Equal(0, result.EventsDeleted);
        Assert.Equal(0, result.ResultsDeleted);
        Assert.Equal(0, result.MessagesDeleted);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
