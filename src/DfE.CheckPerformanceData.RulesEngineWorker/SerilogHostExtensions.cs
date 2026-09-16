using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

/// <summary>
/// Gives the worker the same console output as the web app's <c>UseCpdSerilog</c>: one compact
/// JSON object per event outside Development, so Logit groups a multi-line message and its
/// exception into a single entry rather than one entry per line. Duplicated from the Web project
/// because the worker cannot reference it.
/// </summary>
public static class SerilogHostExtensions
{
    public static WebApplicationBuilder UseCpdSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, config) =>
        {
            var isDevelopment = context.HostingEnvironment.IsDevelopment();

            config
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(isDevelopment
                    ? new ExpressionTemplate(
                        "[{@t:HH:mm:ss} {@l:u3}] {SourceContext}\n  {@m}\n{@x}",
                        theme: TemplateTheme.Code)
                    : new CompactJsonFormatter());
        });

        // Remove the default Console provider so the worker does not print every event a second
        // time as a plain "info: Category[0]" line — the multi-line form Logit splits up.
        builder.Logging.ClearProviders();

        return builder;
    }
}
