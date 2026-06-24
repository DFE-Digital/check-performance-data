using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Persistence;

public static class DependencyManager
{
    public static IServiceCollection AddPersistenceDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PortalDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");

            options
                .UseNpgsql(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure();
                })
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        // Registered unconditionally: the DevDataSeedingOrchestrator (and the admin Danger
        // zone "Reset seed data" action that drives it) is available in every non-Production
        // environment, not just where startup seeding runs. Gating this on the narrower
        // isDevelopmentEnvironment flag left it unresolvable in QA, where the danger zone is
        // live. The seeder is inert until SeedAsync() is called, and its only callers stay
        // gated (startup seedData check + the non-Prod, admin-only danger zone).
        services.AddScoped<DevDataSeeder>();

        // CheckYourPupilDataRepository caches per-school pupil JSON. AddMemoryCache is
        // idempotent, so hosts that already register it (Web) are unaffected.
        services.AddMemoryCache();

        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<IWikiRepository, WikiRepository>();
        services.AddScoped<Application.Settings.ISettingRepository, Repositories.SettingRepository>();
        services.AddScoped<IContentBlockRepository, ContentBlockRepository>();
        services.AddScoped<ILandingPageRepository, LandingPageRepository>();
        services.AddScoped<IWindowRepository, WindowRepository>();
        services.AddScoped<ICheckYourPupilDataRepository, CheckYourPupilDataRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<Application.UncommittedRequests.IUncommittedRequestsRepository,
            Repositories.UncommittedRequestsRepository>();
        services.AddScoped<ICountryRepository, CountryRepository>();
        services.AddScoped<Application.RulesConfig.IRulesConfigVersionRepository,
            Repositories.RulesConfigVersionRepository>();
        services.AddScoped<Application.Observability.IMetricsQueryService,
            Observability.MetricsQueryService>();
        services.AddScoped<Application.Observability.IMetricsSink,
            Observability.DbMetricsSink>();
        services.AddScoped<Application.Observability.IShareTokenService,
            Observability.ShareTokenService>();

        return services;
    }
}
