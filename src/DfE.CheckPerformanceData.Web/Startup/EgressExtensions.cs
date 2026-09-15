using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class EgressExtensions
{
    // Registers the LDS data egress (AB#294553). Review finding B1: the safe default is the real
    // Zendesk client, matching the worker's own configured default (RulesEngineWorker's
    // appsettings.json pins Zendesk:UseFake=false; Web/appsettings.json now does the same) — the
    // dev outbox fake is opt-in only (Zendesk__UseFake=true), and refused outright in Production
    // regardless of what configuration says, the same way DevEgressController is unreachable there.
    public static IServiceCollection AddCpdEgress(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<EgressStorageOptions>(configuration.GetSection(EgressStorageOptions.SectionName));
        services.Configure<ZendeskTicketFieldSettings>(configuration.GetSection(ZendeskTicketFieldSettings.SectionName));

        services.AddScoped<IEgressRunRepository, EgressRunRepository>();
        services.AddScoped<IEgressRunService, EgressRunService>();
        services.AddScoped<IEgressPreprocessor, EgressPreprocessor>();
        services.AddScoped<IEgressTransferService, EgressTransferService>();
        services.AddScoped<IEgressBlobClient, EgressBlobClient>();

        var useFake = configuration.GetValue(SettingKeys.ZendeskUseFake, defaultValue: false);
        if (useFake)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    $"{SettingKeys.ZendeskUseFake}=true is not permitted in Production: the dev " +
                    "outbox ticket source must never be reachable there. Remove the setting (or " +
                    "set it to false) for this environment.");
            }
            services.AddScoped<IEgressTicketSource, DevOutboxEgressTicketSource>();
        }
        else
        {
            services.AddZendeskApiClient(configuration, requireRealClient: true);
            services.AddScoped<IEgressTicketSource, ZendeskEgressTicketSource>();
        }
        return services;
    }
}
