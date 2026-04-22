using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Features.LandingPage;
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

        services.AddScoped<IWikiRepository, WikiRepository>();
        services.AddScoped<IContentBlockRepository, ContentBlockRepository>();
        services.AddScoped<ILandingPageRepository, LandingPageRepository>();
        services.AddScoped<ICheckYourPupilDataRepository, CheckYourPupilDataRepository>();

        return services;
    }
}
