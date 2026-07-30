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

    // Retention setting hot-swap: the job re-reads SearchAnalyticsRetentionDays on
    // every tick, so an admin-driven setting change takes effect on the next tick
    // without a restart. Reproduces by seeding two cohorts (aged 50 d and 20 d),
    // running RunOnceAsync at 90 d retention (both survive), changing the setting
    // to 30 d, and running again — only the 20 d cohort survives. If the job caches
    // the settings value across ticks the second call would purge zero rows and the
    // 50 d cohort would still be there; that failure mode is what the test locks.
    [Fact]
    public async Task RunOnceAsync_ReReadsRetentionSetting_EachTickSoHotSwapTakesEffect()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = DateTime.UtcNow;
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 30,
            oldestAt: now.AddDays(-50));
        await SearchAnalyticsSeedHelpers.SeedSearchEventsAsync(_fixture, 20,
            oldestAt: now.AddDays(-20));

        // Substitute returns 90 on the first read, then 30 on every subsequent read.
        // Matches the "admin edits the setting between ticks" scenario in prod.
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays)
            .Returns(90, 30);
        settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays)
            .Returns(365);

        var sink = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var messageService = new DbSearchMessageService(_fixture.CreateContext());
        var job = new SearchAnalyticsRetentionJob(
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            NullLogger<SearchAnalyticsRetentionJob>.Instance);

        // Tick 1: 90 d retention -> both cohorts survive (50 d and 20 d are within 90 d).
        await job.RunOnceAsync(settings, sink, messageService, CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var afterFirst = await Scalar(conn, "SELECT COUNT(*) FROM search_events");
        Assert.Equal(50L, afterFirst);

        // Tick 2: setting hot-swaps to 30 d — the 50 d cohort must go, the 20 d stays.
        // Re-instantiate sink + messageService with fresh contexts so EF change-tracker
        // state from tick 1 does not shield either from a second delete.
        var sink2 = new DbSearchAnalyticsSink(_fixture.CreateContext());
        var messageService2 = new DbSearchMessageService(_fixture.CreateContext());
        await job.RunOnceAsync(settings, sink2, messageService2, CancellationToken.None);

        var afterSecond = await Scalar(conn, "SELECT COUNT(*) FROM search_events");
        Assert.Equal(20L, afterSecond);

        // Belt-and-braces: the sub was queried at least twice for the retention
        // setting — proves the job re-read the value rather than caching tick 1's.
        await settings.Received(2).GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays);
    }

    private static async Task<long> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)((await cmd.ExecuteScalarAsync())!);
    }
}
