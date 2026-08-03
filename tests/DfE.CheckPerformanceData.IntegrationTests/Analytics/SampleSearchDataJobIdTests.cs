using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the per-run job_id marker that lets Cancel roll back this seed run's
// rows without touching anything else. Locks:
//   * SampleSearchDataSeeder.SeedAsync(jobId=…) stamps job_id on every row it writes
//     across search_events, search_event_results and search_messages.
//   * DbSampleSearchDataGateway.DeleteByJobIdAsync drops all three tables in one
//     transaction, returns per-table counts, and leaves rows belonging to other jobs
//     (or NULL — real user activity) intact.
//   * Round-trip: seed → rollback → tables are back to their pre-seed state.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataJobIdTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataJobIdTests(PostgresFixture fixture)
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

    [Fact]
    public async Task SeedAsync_WritesJobIdOnEveryRow_AcrossThreeTables()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var jobId = Guid.NewGuid();
        var jobIdText = jobId.ToString("N");
        var (seeder, _) = CreateSut();

        await seeder.SeedAsync(
            span: TimeSpan.FromHours(24),
            eventCount: 40,
            messageCount: 4,
            nowUtc: now,
            seed: 55,
            jobId: jobId,
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var eventRowsWithJobId = await ScalarLongAsync(conn,
            $"SELECT COUNT(*) FROM search_events WHERE job_id = '{jobIdText}';");
        var messageRowsWithJobId = await ScalarLongAsync(conn,
            $"SELECT COUNT(*) FROM search_messages WHERE job_id = '{jobIdText}';");
        var childRowsWithJobId = await ScalarLongAsync(conn,
            $"SELECT COUNT(*) FROM search_event_results WHERE job_id = '{jobIdText}';");

        Assert.Equal(40L, eventRowsWithJobId);
        Assert.Equal(4L, messageRowsWithJobId);
        Assert.True(childRowsWithJobId > 0,
            "Every non-zero-result event fires at least one child result row; the seeder " +
            "must stamp job_id on children as well as parents so rollback drops both.");

        // Nothing outside the job_id — the seeder should not stamp anything as NULL.
        var untaggedEvents = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE job_id IS NULL;");
        Assert.Equal(0L, untaggedEvents);
    }

    [Fact]
    public async Task DeleteByJobIdAsync_DropsOnlyMatchingJobRows_InAllThreeTables()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var (seeder, gateway) = CreateSut();

        // Two independent seed runs, each stamped with a distinct job id.
        await seeder.SeedAsync(TimeSpan.FromHours(24), 20, 2, now, 1, CancellationToken.None,
            jobId: jobA);
        var (seeder2, _) = CreateSut();
        await seeder2.SeedAsync(TimeSpan.FromHours(24), 15, 3, now, 2, CancellationToken.None,
            jobId: jobB);

        var beforeEvents = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_events;");
        Assert.Equal(35L, beforeEvents);

        var result = await gateway.DeleteByJobIdAsync(jobA.ToString("N"), CancellationToken.None);

        Assert.Equal(20, result.EventsDeleted);
        Assert.Equal(2, result.MessagesDeleted);
        Assert.True(result.ResultsDeleted >= 0);

        // Job B rows survive.
        var afterEvents = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_events;");
        var afterMessages = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_messages;");
        Assert.Equal(15L, afterEvents);
        Assert.Equal(3L, afterMessages);

        // And every surviving row belongs to job B.
        var jobBEvents = await ScalarLongAsync(
            $"SELECT COUNT(*) FROM search_events WHERE job_id = '{jobB:N}';");
        Assert.Equal(15L, jobBEvents);
    }

    [Fact]
    public async Task DeleteByJobIdAsync_PreservesRealUserRows_WithNullJobId()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var (seeder, gateway) = CreateSut();

        // A "real" user row inserted via the sink directly — no job_id assigned; the DTO
        // leaves the marker null. Must survive the rollback.
        var directSink = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var realEvent = new SearchEventDto(
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
        await directSink.RecordBatchAsync(new[] { realEvent }, CancellationToken.None);

        var jobId = Guid.NewGuid();
        await seeder.SeedAsync(TimeSpan.FromHours(24), 12, 1, now, 7, CancellationToken.None,
            jobId: jobId);

        var beforeEvents = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_events;");
        Assert.Equal(13L, beforeEvents);

        await gateway.DeleteByJobIdAsync(jobId.ToString("N"), CancellationToken.None);

        var afterEvents = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_events;");
        var realStill = await ScalarLongAsync(
            "SELECT COUNT(*) FROM search_events WHERE job_id IS NULL;");
        Assert.Equal(1L, afterEvents);
        Assert.Equal(1L, realStill);
    }

    [Fact]
    public async Task DeleteByJobIdAsync_OnUnknownJobId_ReturnsZeroCounts_WithoutThrowing()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var (_, gateway) = CreateSut();

        var result = await gateway.DeleteByJobIdAsync(Guid.NewGuid().ToString("N"),
            CancellationToken.None);

        Assert.Equal(0, result.EventsDeleted);
        Assert.Equal(0, result.ResultsDeleted);
        Assert.Equal(0, result.MessagesDeleted);
    }

    private async Task<long> ScalarLongAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return await ScalarLongAsync(conn, sql);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
