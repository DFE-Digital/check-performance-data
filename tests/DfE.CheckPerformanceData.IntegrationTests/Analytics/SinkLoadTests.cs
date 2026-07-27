using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Load-proof for the whole emission pipeline: decorator → channel → writer → sink →
// search_events, under 500 concurrent RecordSearch calls. Post-conditions:
//
//   * All 500 events land in search_events (may take a few flush cycles).
//   * The drop counter reads exactly 0 — the 1000-capacity channel absorbs the burst.
//
// Booting the full app via WebApplicationFactory<Program> would require the whole DfE
// Sign-In / Azure Blob / distributed Postgres cache surface — an order of magnitude
// heavier than the load path this test targets. The direct decorator-level assertion
// exercises the same pipeline (the HTTP-to-decorator hop was proven in Phase 1.10's
// search resilience tests). Trait "Slow" excludes it from the fast inner-loop run.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "Slow")]
public sealed class SinkLoadTests
{
    private readonly PostgresFixture _fixture;

    public SinkLoadTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // Stub session provider: every parallel invocation gets the same session id so we
    // can filter the resulting rows.
    private sealed class FixedSessionProvider(string id) : ISearchAnalyticsSessionProvider
    {
        public string? GetSessionId() => id;
    }

    private sealed class FakeSearchDebugOptions : ISearchDebugOptions
    {
        public bool ShowSearchDebug => false;
    }

    [Fact]
    public async Task RecordBatchAsync_UnderFiveHundredConcurrentEnqueues_ProducesFiveHundredRowsAndZeroDrops()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        // Compose the pipeline with the real production channel + sink + a decorator
        // holding a fixed session id. No HTTP hop — the invariant we care about is the
        // decorator/channel/writer/sink chain under concurrent RecordSearch pressure.
        var channel = new SearchAnalyticsChannel();
        var droppedCounter = new SearchAnalyticsDroppedCounter();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, FakeCurrentUserService>();
        services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<ISearchAnalyticsSink, DbSearchAnalyticsSink>();
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var writer = new SearchEventWriter(
            channel, scopeFactory, NullLogger<SearchEventWriter>.Instance,
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromMilliseconds(100));

        var innerLogger = NullLogger<LoggerSearchTelemetry>.Instance;
        var innerCounter = new SearchZeroResultsCounter();
        var inner = new LoggerSearchTelemetry(innerLogger, innerCounter, new FakeSearchDebugOptions());
        var decorator = new SinkAndLogSearchTelemetry(
            channel.Channel.Writer, inner, droppedCounter,
            NullLogger<SinkAndLogSearchTelemetry>.Instance,
            new FixedSessionProvider("load-session"));

        using var cts = new CancellationTokenSource();
        await writer.StartAsync(cts.Token);

        try
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 32 };
            await Parallel.ForEachAsync(Enumerable.Range(0, 500), options,
                (i, _) =>
                {
                    var evt = new SearchTelemetryEvent(
                        SearchId: Guid.NewGuid(),
                        UtcTimestamp: DateTime.UtcNow,
                        QueryRaw: "widget-" + i,
                        QueryNormalised: "widget-" + i,
                        Scope: null,
                        LatencyMsTotal: 5L,
                        LatencyMsPages: 2L,
                        LatencyMsBlocks: 3L,
                        Hits: [],
                        FilterExclusions: []);
                    decorator.RecordSearch(evt);
                    return ValueTask.CompletedTask;
                });

            // Give the writer time to drain 500 rows through 5+ batches at the compressed
            // flush interval.
            await WaitForCountAsync(500, TimeSpan.FromSeconds(15));

            Assert.Equal(0L, droppedCounter.Read());
        }
        finally
        {
            cts.Cancel();
            await writer.StopAsync(CancellationToken.None);
        }
    }

    private async Task WaitForCountAsync(int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long lastSeen = -1;
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM search_events WHERE session_id = 'load-session';";
            lastSeen = (long)((await cmd.ExecuteScalarAsync())!);
            if (lastSeen >= expected)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out after {timeout} waiting for {expected} rows; last seen = {lastSeen}.");
    }
}
