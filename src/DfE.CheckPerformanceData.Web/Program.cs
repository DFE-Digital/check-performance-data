using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Web.Extensions;
using GovUk.Frontend.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using DfE.CheckPerformanceData.Web.Startup;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting application");

    var builder = WebApplication.CreateBuilder(args);

    // WebApplication.CreateBuilder already configures the configuration sources in
    // the correct precedence (last wins): appsettings.json, appsettings.{Environment}.json,
    // user secrets (Development), environment variables, command line. Re-adding
    // appsettings.json here previously landed it after appsettings.{Environment}.json
    // and silently clobbered environment-specific overrides.
    var configuration = builder.Configuration;

    builder.UseCpdSerilog();

    builder.AddCpdCoreWeb();

    builder.Services
        .AddCpdSettings(configuration)
        .AddDfeApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddGovUkFrontend()
        .AddPersistenceDependencies(configuration)
        .AddApplicationDependencies()
        .AddNotifyService(builder.Configuration)
        .AddAdminNavEntries(includeDangerZone: !builder.Environment.IsProduction())
        .AddCpdSearchTelemetry()
        .AddCpdNotifications()
        .AddCpdAppLogSink(configuration)
        .AddCpdQueue(configuration)
        .AddCpdJourneyAndCmsServices()
        .AddCpdBlobStorage(configuration)
        .AddCpdBigQueryAnalytics(configuration);

    builder.AddCpdDevImpersonation();

    builder.AddCpdDataProtection();

    builder.AddCpdSessionStore();

    var app = builder.Build();

    await app.RunCpdStartupTasksAsync();

    app.UseCpdRequestPipeline();

    app.Run();
}
catch (Exception e)
{
    Log.Fatal(e, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();   
}
