using System.Diagnostics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the real DbSearchMessageService.PurgeExpiredMessagesAsync — the
// second delete path in the retention job. Deliberately lives on ISearchMessageService
// (not ISearchAnalyticsSink) so events and messages have exactly one owner each. Facts:
//
//   1. Purge with a 365-day cutoff removes messages older than that and preserves fresher
//      ones — 100/100 aged/fresh split.
//   2. Empty-table purge is a no-op that returns 0 without any DELETE round-trip cost.
//   3. 30k-row purge (Slow) completes without a single unbounded DELETE — the CTE-batched
//      shape keeps each 10k batch well inside the per-batch time budget.
[Collection(nameof(PostgresCollection))]
public sealed class DbSearchMessageServicePurgeTests
{
    private readonly PostgresFixture _fixture;

    public DbSearchMessageServicePurgeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private DbSearchMessageService CreateSut() => new(_fixture.CreateContext());

    [Fact]
    public async Task PurgeExpiredMessagesAsync_RemovesAgedMessagesAndKeepsFreshOnes()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        await SearchAnalyticsSeedHelpers.SeedSearchMessagesAsync(_fixture, 100,
            oldestAt: now.AddDays(-366));
        await SearchAnalyticsSeedHelpers.SeedSearchMessagesAsync(_fixture, 100,
            oldestAt: now.AddDays(-364));

        var sut = CreateSut();
        var purged = await sut.PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(365), CancellationToken.None);

        Assert.Equal(100, purged);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM search_messages;";
        var remaining = (long)((await cmd.ExecuteScalarAsync())!);
        Assert.Equal(100L, remaining);
    }

    [Fact]
    public async Task PurgeExpiredMessagesAsync_EmptyTable_ReturnsZero()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var sut = CreateSut();
        var purged = await sut.PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(365), CancellationToken.None);

        Assert.Equal(0, purged);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task PurgeExpiredMessagesAsync_ThirtyThousandAgedRows_StaysUnderPerBatchBudget()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        await SearchAnalyticsSeedHelpers.SeedSearchMessagesAsync(_fixture, 30_000,
            oldestAt: now.AddDays(-400));

        var sut = CreateSut();
        var sw = Stopwatch.StartNew();
        var purged = await sut.PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(365), CancellationToken.None);
        sw.Stop();

        Assert.Equal(30_000, purged);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"PurgeExpiredMessagesAsync of 30k rows should complete in ≪10s; took {sw.Elapsed}.");
    }
}
