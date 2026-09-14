using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Persistence.Repositories;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class EgressExtensions
{
    // Registers the LDS data egress (AB#294553). The web host had no Zendesk client before this:
    // with Zendesk:UseFake true (the default, so a fresh dev/test stack never talks to real
    // Zendesk) decisions come from the worker's dev outbox and no Zendesk settings are needed;
    // with it false the real Refit client is registered here exactly as the worker registers it.
    public static IServiceCollection AddCpdEgress(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EgressStorageOptions>(configuration.GetSection(EgressStorageOptions.SectionName));
        services.Configure<ZendeskTicketFieldSettings>(configuration.GetSection(ZendeskTicketFieldSettings.SectionName));

        services.AddScoped<IEgressRunRepository, EgressRunRepository>();
        services.AddScoped<IEgressRunService, EgressRunService>();
        services.AddScoped<IEgressPreprocessor, EgressPreprocessor>();
        services.AddScoped<IEgressTransferService, EgressTransferService>();
        services.AddScoped<IEgressBlobClient, EgressBlobClient>();

        var useFake = configuration.GetValue(SettingKeys.ZendeskUseFake, defaultValue: true);
        if (useFake)
        {
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
