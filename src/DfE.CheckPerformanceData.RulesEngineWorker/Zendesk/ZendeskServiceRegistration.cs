using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;

// Selects the fake/outbox Zendesk service when the Zendesk:UseFake flag is set, replacing the
// real client registered by AddZendeskApiClient. The flag defaults to true so a fresh dev or
// test environment never pushes to real Zendesk; flipping it to false routes to the real client
// with no code change. The selection is gated on configuration rather than the environment name
// because the DfE test site runs ASPNETCORE_ENVIRONMENT=Development, which an environment-name
// gate cannot distinguish from local development.
public static class ZendeskServiceRegistration
{
    // Defaults to true when the flag is absent: a fresh dev/test environment keeps the fake.
    public static bool ShouldUseFake(IConfiguration configuration) =>
        configuration.GetValue(SettingKeys.ZendeskUseFake, defaultValue: true);

    public static void ConfigureFakeZendesk(IServiceCollection services, IConfiguration configuration)
    {
        if (ShouldUseFake(configuration))
        {
            services.Replace(
                ServiceDescriptor.Scoped<IZendeskService, DevOutboxZendeskService>());
        }
    }
}
