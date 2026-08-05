using DfE.CheckPerformanceData.Web.Settings;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class SettingsExtensions
{
    public static IServiceCollection AddCpdSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GtmSettings>(configuration.GetSection("GoogleTagManager"));
        services.Configure<ClaritySettings>(configuration.GetSection("Clarity"));
        services.Configure<DfE.CheckPerformanceData.Application.Dashboard.DashboardSettings>(
            configuration.GetSection("Dashboard"));

        string? newtonsoftLicenseKey = configuration
            .GetSection("NewtonsoftLicenseKey")
            .Get<string>() ?? null;

        if (newtonsoftLicenseKey is not null)
        {
            License.RegisterLicense(newtonsoftLicenseKey);
        }

        return services;
    }
}
