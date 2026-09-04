using Serilog;
using Serilog.Formatting.Compact;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Startup;
using GovUk.Frontend.AspNetCore;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting application");

    var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
    
    var configuration = builder.Configuration;

    builder.UseCpdSerilog();

    builder.AddCpdCoreWeb();

    builder.Services
        .AddCpdSettings(configuration)
        .AddDfeApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddCpdGovUkFrontend()
        .AddPersistenceDependencies(configuration)
        .AddApplicationDependencies()
        .AddNotifyService(builder.Configuration)
        .AddAdminNavEntries(
            includeResetSeedData: !builder.Environment.IsProduction(),
            // Same whitelist TestDataController enforces, so the tile and the page it links
            // to appear and disappear together instead of the tile leading to a 404.
            includeSampleSearchData: builder.Environment.IsSampleDataAdminEnvironment())
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
catch (System.Exception e)
{
    Log.Fatal(e, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();   
}
