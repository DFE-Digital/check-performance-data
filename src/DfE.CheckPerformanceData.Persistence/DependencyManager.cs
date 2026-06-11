using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.Wiki;
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
        IConfiguration configuration, 
        bool isDevelopmentEnvironment = false)
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

        if(isDevelopmentEnvironment)
            services.AddScoped<DevDataSeeder>();

        // CheckYourPupilDataRepository caches per-school pupil JSON. AddMemoryCache is
        // idempotent, so hosts that already register it (Web) are unaffected.
        services.AddMemoryCache();

        services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<IWikiRepository, WikiRepository>();
        services.AddScoped<Application.Settings.ISettingRepository, Repositories.SettingRepository>();
        services.AddScoped<IContentBlockRepository, ContentBlockRepository>();
        services.AddScoped<ILandingPageRepository, LandingPageRepository>();
        services.AddScoped<ICheckYourPupilDataRepository, CheckYourPupilDataRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<Application.RequestDecision.IDecisionOutcomeRepository, DecisionOutcomeRepository>();
        services.AddScoped<ICountryRepository, CountryRepository>();

        return services;
    }
}
