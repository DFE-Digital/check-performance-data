using System.Net;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Health;

/// <summary>
/// Exercises the worker's HTTP health surface against the same registration code the
/// worker process uses. Liveness reports process-up unconditionally; readiness reflects
/// database reachability so a crash-looping or DB-isolated worker is detectable.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WorkerHealthTests
{
    private readonly PostgresFixture _fixture;

    public WorkerHealthTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReadinessReturnsUnhealthyWhenDatabaseUnreachable()
    {
        // Point the worker DbContext at a database that cannot be reached so the
        // readiness check's connectivity probe fails.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=1;Command Timeout=1";

        using var host = await BuildHealthHostAsync(unreachable, RulesHealth.Healthy);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/healthz/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessReturnsHealthyWhenDatabaseReachable()
    {
        using var host = await BuildHealthHostAsync(_fixture.ConnectionString, RulesHealth.Healthy);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/healthz/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LivenessReturnsHealthyEvenWhenDatabaseUnreachable()
    {
        // Liveness must reflect only that the process is up; an unreachable database
        // must not restart-loop the container via the liveness probe.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=1;Command Timeout=1";

        using var host = await BuildHealthHostAsync(unreachable, RulesHealth.Healthy);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/healthz/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessDoesNotLeakConnectionStringsOrPayload()
    {
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=secretdb;Username=secretuser;Password=secretpass;Timeout=1;Command Timeout=1";

        using var host = await BuildHealthHostAsync(unreachable, RulesHealth.Healthy);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/healthz/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("secretpass", body);
        Assert.DoesNotContain("secretuser", body);
        Assert.DoesNotContain("secretdb", body);
    }

    private static async Task<IHost> BuildHealthHostAsync(string connectionString, RulesHealth rulesHealth)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddSingleton<ICurrentUserService, FakeCurrentUserService>();
                services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(connectionString));
                services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                services.AddScoped<IQueueService, PostgresQueueService>();

                var snapshot = new RulesSnapshot(
                    new RuleSet("test", DateTimeOffset.UtcNow, Array.Empty<OutcomeRules>()),
                    Lookups.Empty,
                    "test",
                    DateTimeOffset.UtcNow,
                    rulesHealth);
                var provider = Substitute.For<IRulesProvider>();
                provider.Current.Returns(snapshot);
                services.AddSingleton(provider);

                services.AddRouting();
                services.AddWorkerHealthChecks();
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapWorkerHealthChecks());
            });
        });

        var host = await builder.StartAsync();
        return host;
    }
}
