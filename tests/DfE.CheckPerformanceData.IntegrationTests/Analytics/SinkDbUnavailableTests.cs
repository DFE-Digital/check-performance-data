using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// DB-outage pin for the sink layer. Runs against a PRIVATE Postgres container so
// stopping mid-test does not poison the shared PostgresCollection scope that other
// integration tests depend on. Verifies that when the database is stopped mid-flight,
// the sink SURFACES the connection failure to its caller rather than silently
// swallowing — the writer's job is to swallow, not the sink's. The complementary
// "writer stays alive when the sink throws" invariant lives in SearchEventWriterTests
// (its ExecuteAsync_WhenSinkThrowsOnFirstBatch fact drives it directly via a
// ThrowingOnceSink, which is functionally identical to a DB blip from the writer's
// perspective and avoids the Testcontainers port-remapping / Npgsql connection-pool
// staleness gotchas that a stop-and-restart test would run into).
public sealed class SinkDbUnavailableTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // Cached at start-up so a mid-test StopAsync doesn't tear down the port mapping the
    // connection string depends on — Testcontainers throws "Exposed port is not mapped"
    // if you try to read GetConnectionString() while the container is stopped.
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        // Migrate once with the container up so the search_events schema exists for the
        // "back-up" phase of the writer test.
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_connectionString)
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
}
