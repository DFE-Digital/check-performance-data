using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""WikiPages"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
