using Serilog.Formatting.Compact;
using Serilog.Templates;
using Serilog.Templates.Themes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class SerilogHostExtensions
{
    public static WebApplicationBuilder UseCpdSerilog(this WebApplicationBuilder builder)
    {
        // writeToProviders: true so ILogger events are forwarded to every ILoggerProvider registered
        // through DI (Serilog does not become the exclusive sink). Required for the Postgres
        // DatabaseLoggerProvider to receive events; without it Serilog handles everything itself
        // and other providers silently receive nothing.
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
        }, writeToProviders: true);

        // WebApplication.CreateBuilder registers a default Console provider. With Serilog's
        // writeToProviders: true, that default provider ALSO receives every event and prints a
        // second copy (the "info: Category[0]" line) alongside Serilog's own console sink.
        // Clear the defaults so Serilog owns the console; the additive DatabaseLoggerProvider is
        // registered later (below) and is unaffected, since ClearProviders only removes what is
        // already registered.
        builder.Logging.ClearProviders();

        return builder;
    }
}
