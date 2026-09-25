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
    // Registers the LDS data egress (AB#294553). The safe default is the real Zendesk client,
    // matching the worker's own configured default — a fresh environment reads real egress
    // decisions, never the dev outbox. The dev outbox is opt-in only via Egress:UseDevOutbox
    // (Egress__UseDevOutbox as an environment variable): local/E2E stacks and the review app set
    // it so /dev/egress/seed fixture ids resolve in the DevZendeskTickets table. It is
    // deliberately separate from Zendesk:UseFake, which selects the worker's ticket-WRITE
    // service (real vs outbox) — decoupling the two lets a review app write real Zendesk
    // tickets while still reading egress decisions from the outbox. It is refused outright in
    // Production regardless of what configuration says, the same way DevEgressController is
    // unreachable there.
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

        var useDevOutbox = configuration.GetValue(SettingKeys.EgressUseDevOutbox, defaultValue: false);
        if (useDevOutbox)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    $"{SettingKeys.EgressUseDevOutbox}=true is not permitted in Production: the dev " +
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
