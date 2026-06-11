using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.RulesEngineWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RulesEngineOptions>(builder.Configuration.GetSection("RulesEngineOptions"));

builder.Services.AddSingleton(sp => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
    new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }));

builder.Services.AddZendeskApiClient(builder.Configuration);
builder.Services.AddInfrastructureDependencies(builder.Configuration);
// Persistence is registered so the worker can write engine decisions back to
// the ChangeRequests table (IDecisionOutcomeRepository). PortalDbContext needs
// an ICurrentUserService for audit stamping; the worker supplies a system identity.
builder.Services.AddSingleton<DfE.CheckPerformanceData.Application.CurrentUser.ICurrentUserService, WorkerCurrentUserService>();
builder.Services.AddPersistenceDependencies(builder.Configuration);
// Deliberately not AddApplicationDependencies(): the full Application set needs
// Web-registered clients this host doesn't have.
builder.Services.AddRulesEngineDependencies();
builder.Services.AddRulesProvider(builder.Configuration);

builder.Services.AddHostedService<RulesEngineWorker>();

var host = builder.Build();
host.Run();