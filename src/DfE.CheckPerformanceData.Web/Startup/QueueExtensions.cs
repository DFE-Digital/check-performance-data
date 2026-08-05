using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Infrastructure.Queue;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class QueueExtensions
{
    public static IServiceCollection AddCpdQueue(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueOptions>(configuration.GetSection("QueueOptions"));
        services.AddScoped<IQueueService, PostgresQueueService>();
        services.AddScoped<IQueueAdminService, QueueAdminService>();
        services.AddScoped<DfE.CheckPerformanceData.Application.Observability.SubmittedMetricRecorder>();
        services.AddSingleton<PayloadRedactor>();

        return services;
    }
}
