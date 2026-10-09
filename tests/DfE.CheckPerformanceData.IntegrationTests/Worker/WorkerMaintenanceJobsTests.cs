using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.RulesEngineWorker;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Worker;

/// <summary>
/// Runs each maintenance job's per-tick path through the container the worker actually builds.
/// The jobs resolve their collaborators from a fresh scope on every tick rather than through
/// their constructors, so neither host startup nor the jobs' own unit tests (which inject the
/// collaborators directly) notice a registration the worker forgot: the job just logs an error
/// once an hour and does nothing. That is how the content-staging session sweep went unrun from
/// #354 onwards.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WorkerMaintenanceJobsTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    // Never contacted: the blob and rules clients are only constructed, and no tick here reads
    // a blob. Present because the worker's registrations expect it, as they do in every
    // environment the worker runs in.
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private ServiceProvider BuildWorkerContainer()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fixture.ConnectionString,
            ["ConnectionStrings:AzureStorage"] = AzuriteConnection,
            // The section every environment carries. The fake Zendesk is the default, so no
            // credentials are needed and nothing is sent.
            ["ZendeskSettings:Subdomain"] = "dfe",
            ["ZendeskSettings:Domain"] = "zendesk",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddWorkerServices(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static Func<IServiceProvider, CancellationToken, Task>? TickOf(IHostedService job) => job switch
    {
        DlqRetentionJob j => j.RunOnceAsync,
        MetricsRetentionJob j => j.RunOnceAsync,
        SearchAnalyticsRetentionJob j => j.RunOnceAsync,
        ContentStagingSessionRetentionJob j => j.RunOnceAsync,
        _ => null,
    };

    [Fact]
    public async Task Every_maintenance_job_completes_a_tick_against_the_worker_container()
    {
        await using var provider = BuildWorkerContainer();

        var jobs = provider.GetServices<IHostedService>()
            .Where(h => h.GetType().Namespace == typeof(DlqRetentionJob).Namespace)
            .ToList();

        Assert.NotEmpty(jobs);
        foreach (var job in jobs)
        {
            // A new job needs a case in TickOf; failing here is what makes sure it gets one.
            var tick = TickOf(job);
            Assert.True(tick is not null, $"{job.GetType().Name} has no tick wired into this test.");

            await using var scope = provider.CreateAsyncScope();
            await tick!(scope.ServiceProvider, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Session_retention_tick_deletes_an_expired_session()
    {
        var expiredId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var created = DateTime.UtcNow.AddDays(-2);
            seed.ContentStagingSessions.Add(new ContentStagingSession
            {
                Id = expiredId,
                BundleJson = "{}",
                CreatedBy = "worker-retention-test@education.gov.uk",
                CreatedAtUtc = created,
                ExpiresAtUtc = created.AddHours(1),
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildWorkerContainer();
        var job = provider.GetServices<IHostedService>().OfType<ContentStagingSessionRetentionJob>().Single();

        await using (var scope = provider.CreateAsyncScope())
        {
            await job.RunOnceAsync(scope.ServiceProvider, CancellationToken.None);
        }

        await using var ctx = _fixture.CreateContext();
        Assert.False(await ctx.ContentStagingSessions.AnyAsync(s => s.Id == expiredId));
    }
}
