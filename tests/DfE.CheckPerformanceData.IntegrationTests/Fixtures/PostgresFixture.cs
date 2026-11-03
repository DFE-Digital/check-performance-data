using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Stops the underlying Postgres container mid-fixture-scope. Corresponds to
    /// <c>docker stop</c> (SIGTERM then SIGKILL after grace). Migrations persist across a
    /// stop/start cycle on the same container instance — the <c>postgres:17-alpine</c>
    /// data volume survives, so callers do not need to re-run migrations after restart.
    /// Callers MUST call <see cref="StartAsync"/> in their test's DisposeAsync to
    /// unpoison subsequent tests in the same PostgresCollection scope.
    /// </summary>
    public Task StopAsync() => _postgres.StopAsync();

    /// <summary>
    /// Restarts a previously-stopped container. Idempotent: calling on an already-running
    /// container is a no-op per the Testcontainers-dotnet contract, so this is safe to
    /// call unconditionally in a test's disposal path even if the test aborted before its
    /// own <see cref="StopAsync"/>.
    /// </summary>
    public Task StartAsync() => _postgres.StartAsync();

    public PortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            // Mirror production: both the web host and the rules-engine worker enable retry-on-failure,
            // which installs a retrying execution strategy. ExecuteInTransactionAsync wraps its
            // BeginTransaction inside that strategy and its re-entrant join must hold under it, so the
            // integration tests exercise the same strategy production runs rather than the non-retrying
            // default — otherwise the transactional code path here never matches the deployed one.
            .UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure())
            .Options;
        return new PortalDbContext(options, new FakeCurrentUserService());
    }

    /// <summary>
    /// Same context as <see cref="CreateContext"/>, with one EF interceptor, a 1 ms cap on the
    /// retry back-off and 3 retries rather than the default 6. Both limits keep a forced-retry
    /// test fast; the Npgsql retrying strategy and its transient classification match production.
    /// </summary>
    public PortalDbContext CreateRetryingContext(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1), errorCodesToAdd: null))
            .AddInterceptors(interceptor)
            .Options;
        return new PortalDbContext(options, new FakeCurrentUserService());
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
