using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

public static class NotifyServiceRegistration
{
    public static bool ShouldUseFake(IConfiguration configuration) =>
        configuration.GetValue(SettingKeys.NotifyUseFake, defaultValue: true);

    public static void ConfigureFakeNotify(IServiceCollection services, IConfiguration configuration)
    {
        if (ShouldUseFake(configuration))
        {
            services.Replace(
                ServiceDescriptor.Singleton<INotifyService, DevConsoleNotifyService>());
        }
    }
}
