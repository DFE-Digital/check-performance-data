using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// End-to-end retention proof: real sink + real message service + real Postgres. Seed
// four cohorts (100 events aged 91 days, 100 aged 89 days, 20 messages aged 370 days,
// 20 aged 360 days), drive the retention job through its direct RunOnceAsync overload
// with defaults (events=90d, messages=365d), and assert only the fresh cohort of each
// table survives. Proves both purge paths land against a real DB in one tick without
// touching each other's table.
[Collection(nameof(PostgresCollection))]
public sealed class SearchAnalyticsRetentionJobTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsRetentionJobTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunOnceAsync_WithDefaults_PurgesAgedEventsAndAgedMessagesInSameTick()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 100,
            oldestAt: now.AddDays(-91));
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 100,
            oldestAt: now.AddDays(-89));
        await SearchAnalyticsSeedHelpers.SeedSearchMessagesAsync(_fixture, 20,
            oldestAt: now.AddDays(-370));
        await SearchAnalyticsSeedHelpers.SeedSearchMessagesAsync(_fixture, 20,
            oldestAt: now.AddDays(-360));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays).Returns(90);
        settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays).Returns(365);

        var sink = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var messageService = new DbSearchMessageService(_fixture.CreateContext());

        var job = new SearchAnalyticsRetentionJob(
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            NullLogger<SearchAnalyticsRetentionJob>.Instance);

        await job.RunOnceAsync(settings, sink, messageService, CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var eventCount = await Scalar(conn, "SELECT COUNT(*) FROM search_events");
        var messageCount = await Scalar(conn, "SELECT COUNT(*) FROM search_messages");

        Assert.Equal(100L, eventCount);
        Assert.Equal(20L, messageCount);
    }

    private static async Task<long> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)((await cmd.ExecuteScalarAsync())!);
    }
}
