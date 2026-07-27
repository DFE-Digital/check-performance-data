using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// DB-outage pin for the sink + writer chain. Runs against a PRIVATE Postgres container
// so stopping mid-test does not poison the shared PostgresCollection scope that other
// integration tests depend on.
//
// Two facts:
//   1. When the DB is down and RecordBatchAsync is invoked directly, the sink surfaces
//      the connection failure as an exception (does NOT silently swallow it) — the
//      writer's job is to swallow, not the sink's.
//   2. When the DB is down and the writer picks up an enqueued DTO, the writer's own
//      catch-log-continue path keeps it alive; once the container comes back a
//      subsequent enqueue lands normally. This is the "background service must never
//      tear down on a transient DB blip" invariant.
public sealed class SinkDbUnavailableTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Migrate once with the container up so the search_events schema exists for the
        // "back-up" phase of the writer test.
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options, new FakeCurrentUserService());

    [Fact]
    public async Task RecordBatchAsync_WithStoppedDatabase_ThrowsWithoutMaskingTheFailure()
    {
        await _postgres.StopAsync();
        try
        {
            var sink = new DbSearchAnalyticsSink(CreateContext());
            var dto = new SearchEventDto(
                DateTime.UtcNow, "outage", "q", "q", null, 0, 0, 1, []);

            await Assert.ThrowsAnyAsync<Exception>(
                () => sink.RecordBatchAsync([dto], CancellationToken.None));
        }
        finally
        {
            await _postgres.StartAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseGoesDownAndComesBack_SurvivesAndDrainsSubsequentBatches()
    {
        // Compose the pipeline with the private container's connection string.
        var channel = new SearchAnalyticsChannel();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, FakeCurrentUserService>();
        services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<ISearchAnalyticsSink, DbSearchAnalyticsSink>();
        await using var provider = services.BuildServiceProvider();

        var writer = new SearchEventWriter(
            channel, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchEventWriter>.Instance,
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromMilliseconds(100));

        using var cts = new CancellationTokenSource();
        await writer.StartAsync(cts.Token);

        try
        {
            // Stop the DB, enqueue a doomed event.
            await _postgres.StopAsync();
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                DateTime.UtcNow, "outage-1", "q", "q", null, 0, 0, 1, []));

            // Let the writer take at least one failed swing.
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Bring the DB back and enqueue a survivor.
            await _postgres.StartAsync();

            // Recreate the connection-pool by discarding the DI provider's cached context
            // — pooled connections that were open at StopAsync are stale and would keep
            // failing until they get evicted. Simulate that by writing through a new
            // scope on the surviving pipeline.
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                DateTime.UtcNow, "survivor-1", "q", "q", null, 0, 0, 1, []));
            channel.Channel.Writer.TryWrite(new SearchEventDto(
                DateTime.UtcNow, "survivor-2", "q", "q", null, 0, 0, 1, []));

            // Poll for the survivors to land — the writer's retry loop keeps its cadence,
            // so once the pool refreshes the batches flow again.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            long survivorCount = -1;
            while (DateTime.UtcNow < deadline)
            {
                await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
                try { await conn.OpenAsync(); }
                catch { await Task.Delay(200); continue; }
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT COUNT(*) FROM search_events WHERE session_id LIKE 'survivor-%';";
                try { survivorCount = (long)((await cmd.ExecuteScalarAsync())!); }
                catch { survivorCount = -1; }
                if (survivorCount >= 2) return;
                await Task.Delay(200);
            }

            throw new Xunit.Sdk.XunitException(
                $"Writer failed to recover after DB restart; last survivor count = {survivorCount}.");
        }
        finally
        {
            cts.Cancel();
            await writer.StopAsync(CancellationToken.None);
        }
    }
}
