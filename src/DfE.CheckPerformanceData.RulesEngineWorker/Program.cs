using DfE.CheckPerformanceData.RulesEngineWorker;
using DfE.CheckPerformanceData.RulesEngineWorker.Health;
using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Formatting.Compact;

// Bootstrap logger so a failure before the host is built is still one JSON entry in Logit.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting worker");

    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddUserSecrets<Program>();
    builder.UseCpdSerilog();

    builder.Services.AddWorkerServices(builder.Configuration);

    builder.Services.AddWorkerHealthChecks();

    var app = builder.Build();
    app.MapWorkerHealthChecks();
    app.Run();
}
catch (Exception e)
{
    Log.Fatal(e, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
