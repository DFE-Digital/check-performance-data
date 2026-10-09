using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

/// <summary>
/// The worker's whole service graph, out of the top-level <c>Program</c> so a test can build the
/// container the worker actually runs with. The maintenance jobs resolve their collaborators from
/// a fresh scope on each tick, not through their constructors, so a missing registration passes
/// host startup and only surfaces as an error logged once an hour (#354's session store did
/// exactly that). Building this container in a test is what catches it.
/// </summary>
public static class WorkerServiceExtensions
{
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueOptions>(configuration.GetSection("QueueOptions"));
        services.AddScoped<IQueueService, PostgresQueueService>();
        services.AddScoped<IQueueAdminService, QueueAdminService>();

        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<ISettingService, SettingService>();

        services.AddScoped<IMetricsSink, DbMetricsSink>();
        // Search analytics retention: the events sink + the messages service are the two purge
        // dependencies of SearchAnalyticsRetentionJob below. Registered sibling to IMetricsSink
        // rather than through AddPersistenceDependencies — the worker deliberately opts out of
        // the shared registration bundle so its manual DbContext registration (lines below) is
        // the single source of truth.
        services.AddScoped<ISearchAnalyticsSink, DbSearchAnalyticsSink>();
        services.AddScoped<ISearchMessageService, DbSearchMessageService>();
        // ContentStagingSessionRetentionJob's purge dependency, for the same reason.
        services.AddScoped<IContentStagingSessionStore, ContentStagingSessionStore>();

        services.AddSingleton<ICurrentUserService, WorkerCurrentUserService>();
        services.AddDbContext<PortalDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"),
                sql => sql.EnableRetryOnFailure()));
        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());

        // Whether the real Zendesk client is the one that will actually serve requests decides how
        // strict its configuration has to be. With the fake selected — the default, so that a fresh dev
        // or test environment never pushes to real Zendesk — blank settings are expected rather than an
        // error, and demanding them would stop the worker starting in exactly the environment the fake
        // exists to serve. The consumers and the retention jobs share this process, so that is all of
        // them, over an integration none of them is using.
        services.AddZendeskApiClient(
            configuration,
            requireRealClient: !ZendeskServiceRegistration.ShouldUseFake(configuration));
        services.AddInfrastructureDependencies(configuration);
        services.AddNotifyService(configuration);

        // When Zendesk:UseFake is set the real Zendesk service is replaced with a fake that captures
        // "created" tickets into the shared dev outbox table, so the rules-engine pipeline can be
        // driven and observed without a real Zendesk. The flag defaults to true so a fresh dev or test
        // environment never pushes to real Zendesk; setting it to false routes to the real client.
        // Gated on configuration rather than the environment name because the test site also runs as
        // Development.
        ZendeskServiceRegistration.ConfigureFakeZendesk(services, configuration);

        // The worker only needs the rules-engine pieces from the Application layer, not the
        // portal's full service graph (which depends on web-only collaborators).
        services.AddSingleton<IRulesEngine, RulesEngine>();
        services.AddSingleton<IRuleContextMapper, RuleContextMapper>();
        services.AddSingleton<RuleSetValidator>();

        services.AddRulesProvider(configuration);

        services.AddHostedService<RulesConsumer>();
        services.AddHostedService<ZendeskConsumer>();
        services.AddHostedService(sp =>
            new DlqRetentionJob(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<DlqRetentionJob>>()));
        services.AddHostedService(sp =>
            new MetricsRetentionJob(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<MetricsRetentionJob>>()));
        services.AddHostedService(sp =>
            new SearchAnalyticsRetentionJob(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<SearchAnalyticsRetentionJob>>()));
        services.AddHostedService(sp =>
            new ContentStagingSessionRetentionJob(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<ContentStagingSessionRetentionJob>>()));

        return services;
    }
}
