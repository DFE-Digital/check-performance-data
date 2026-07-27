using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// End-to-end verification of the background-service drain path: SearchAnalyticsChannel
// → SearchEventWriter → DbSearchAnalyticsSink → search_events. Two facts:
//
//   1. Writer drains everything eventually. Enqueue 250 DTOs, wait for the flush window
//      to burn through them, assert all 250 land in search_events. The default batch
//      size is 100, so the writer takes at least three flush cycles — the test uses a
//      compressed flush interval so it doesn't dominate CI wall time.
//   2. Writer survives a sink exception. A "throws once" sink decorator wraps the real
//      one; the writer catches, logs, waits its retry delay, tries the next batch, and
//      the second batch's rows land normally. The first batch's rows are lost by design
//      (the writer clears its buffer on failure — DatabaseLogWriter precedent).
[Collection(nameof(PostgresCollection))]
public sealed class SearchEventWriterTests
{
    private readonly PostgresFixture _fixture;

    public SearchEventWriterTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // Real DI container so the writer's per-flush scope resolves ISearchAnalyticsSink +
    // IPortalDbContext just like production. Sink override is optional so the throw-once
    // fact can substitute its own.
    private ServiceProvider BuildServices(Func<IServiceProvider, ISearchAnalyticsSink>? sinkFactory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, FakeCurrentUserService>();
        services.AddDbContext<PortalDbContext>(o =>
            o.UseNpgsql(_fixture.ConnectionString));
        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        if (sinkFactory is null)
        {
            services.AddScoped<ISearchAnalyticsSink, DbSearchAnalyticsSink>();
        }
        else
        {
            services.AddScoped(sinkFactory);
        }
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ExecuteAsync_DrainsAllEnqueuedEvents_ToSearchEventsTable()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var channel = new SearchAnalyticsChannel();
        await using var services = BuildServices();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

        // Short flush cadence so the drain of 250 items finishes inside a test budget.
        var writer = new SearchEventWriter(
            channel, scopeFactory, NullLogger<SearchEventWriter>.Instance,
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromMilliseconds(100));

        using var cts = new CancellationTokenSource();
        await writer.StartAsync(cts.Token);

        try
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 250; i++)
            {
                var dto = new SearchEventDto(
                    OccurredAtUtc: now.AddSeconds(i),
                    SessionId: "writer-e2e",
                    QueryRaw: "q-" + i,
                    QueryNormalised: "q-" + i,
                    Scope: null,
                    ResultsPages: 0,
                    ResultsBlocks: 0,
                    LatencyMs: 1,
                    Results: []);
                Assert.True(channel.Channel.Writer.TryWrite(dto),
                    $"Enqueue of DTO {i} must fit inside the default 1000-capacity channel.");
            }

            // Poll until all 250 rows land or the budget expires.
            await WaitForCountAsync(
                predicate: c => c >= 250,
                whereClause: "session_id = 'writer-e2e'",
                timeout: TimeSpan.FromSeconds(10));
        }
        finally
        {
            cts.Cancel();
            await writer.StopAsync(CancellationToken.None);
        }
    }

    // The writer must survive a sink exception without tearing down. First flush throws;
    // the second (after the retry delay) succeeds. Post-condition: the writer's Task is
    // still running AND the second batch's rows land.
    [Fact]
    public async Task ExecuteAsync_WhenSinkThrowsOnFirstBatch_LogsAndContinues()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var channel = new SearchAnalyticsChannel();

        var callCount = 0;
        await using var services = BuildServices(sp => new ThrowingOnceSink(
            inner: new DbSearchAnalyticsSink(sp.GetRequiredService<IPortalDbContext>()),
            incrementAndReturnEntryNumber: () => Interlocked.Increment(ref callCount)));
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

        var writer = new SearchEventWriter(
            channel, scopeFactory, NullLogger<SearchEventWriter>.Instance,
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(50),
            retryDelay: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource();
        await writer.StartAsync(cts.Token);

        try
        {
            var now = DateTime.UtcNow;
            // Enqueue a first batch that WILL throw when flushed.
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                now, "throw-1", "boom", "boom", null, 0, 0, 1, []));

            // Give the writer time to attempt the throw + retry + wait.
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            // Second batch: after the retry delay this must land.
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                now.AddSeconds(1), "keep-me-1", "success", "success", null, 0, 0, 1, []));
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                now.AddSeconds(2), "keep-me-2", "success", "success", null, 0, 0, 1, []));

            await WaitForCountAsync(
                predicate: c => c >= 2,
                whereClause: "session_id LIKE 'keep-me-%'",
                timeout: TimeSpan.FromSeconds(10));

            // The throwing sink was called at least twice — once (thrown) then once
            // (succeeded) — proving the writer did not tear down after the exception.
            Assert.True(callCount >= 2,
                $"Expected the sink to be called at least twice (thrown then succeeded); saw {callCount}.");
        }
        finally
        {
            cts.Cancel();
            await writer.StopAsync(CancellationToken.None);
        }
    }

    private async Task WaitForCountAsync(
        Func<long, bool> predicate, string whereClause, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long lastSeen = -1;
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM search_events WHERE {whereClause};";
            lastSeen = (long)((await cmd.ExecuteScalarAsync())!);
            if (predicate(lastSeen))
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out after {timeout} waiting for count predicate; last seen count = {lastSeen}.");
    }

    // Composes with a real sink; increments the caller-supplied counter every time
    // RecordBatchAsync is entered so the test can assert "sink was called more than
    // once" (proving the writer survived the first throw). Uses an out-of-scope counter
    // (via the shared `increment` action) because the writer resolves a NEW sink from
    // its per-flush scope, so instance state on the wrapper would reset per batch.
    private sealed class ThrowingOnceSink : ISearchAnalyticsSink
    {
        private readonly ISearchAnalyticsSink _inner;
        private readonly Func<int> _incrementAndReturnEntryNumber;

        public ThrowingOnceSink(ISearchAnalyticsSink inner, Func<int> incrementAndReturnEntryNumber)
        {
            _inner = inner;
            _incrementAndReturnEntryNumber = incrementAndReturnEntryNumber;
        }

        public Task RecordBatchAsync(
            IReadOnlyList<SearchEventDto> events, CancellationToken cancellationToken)
        {
            var entry = _incrementAndReturnEntryNumber();
            if (entry == 1)
            {
                throw new InvalidOperationException(
                    "Simulated sink failure on the first batch — writer must survive.");
            }
            return _inner.RecordBatchAsync(events, cancellationToken);
        }

        public Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken) =>
            _inner.PurgeExpiredAsync(olderThan, cancellationToken);
    }
}
