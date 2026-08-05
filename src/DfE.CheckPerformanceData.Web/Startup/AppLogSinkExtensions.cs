using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Web.Logging;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class AppLogSinkExtensions
{
    public static IServiceCollection AddCpdAppLogSink(this IServiceCollection services, IConfiguration configuration)
    {
        // Postgres log sink. The provider is additive: Serilog / console keep working. Options
        // bind from AppLogSink:{MinLevel,BatchSize,FlushInterval,…}; sensible defaults apply if
        // the section is missing.
        var logSinkOptions = new DfE.CheckPerformanceData.Application.Logging.AppLogSinkOptions();
        configuration.GetSection(DfE.CheckPerformanceData.Application.Logging.AppLogSinkOptions.SectionName)
            .Bind(logSinkOptions);
        services.AddSingleton(logSinkOptions);
        services.AddSingleton<DfE.CheckPerformanceData.Application.Logging.AppLogChannel>();
        // Singleton because DatabaseLoggerProvider is a singleton. Under the hood it reads
        // IHttpContextAccessor.HttpContext (backed by AsyncLocal) so per-request path / user
        // / correlation resolve correctly. Background-service logs (no request in flight)
        // simply get nulls.
        services.AddSingleton<DfE.CheckPerformanceData.Application.Logging.ILogRequestContext,
            DfE.CheckPerformanceData.Web.Logging.HttpLogRequestContext>();
        // The provider is a singleton that resolves the shared channel + options from DI. Registering
        // it as ILoggerProvider hooks it into the ambient logger factory alongside console/Serilog.
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider,
            DfE.CheckPerformanceData.Application.Logging.DatabaseLoggerProvider>();
        services.AddHostedService<DfE.CheckPerformanceData.Web.Logging.DatabaseLogWriter>();

        return services;
    }
}
