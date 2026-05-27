using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.RulesEngineWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();

builder.Services.Configure<RulesEngineOptions>(builder.Configuration.GetSection("RulesEngineOptions"));

builder.Services.AddSingleton(sp => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
    new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05){
        MessageEncoding = QueueMessageEncoding.Base64
    }));

builder.Services.AddZendeskApiClient(builder.Configuration);
builder.Services.AddInfrastructureDependencies(builder.Configuration);
builder.Services.AddApplicationDependencies();
builder.Services.AddRulesProvider(builder.Configuration);

builder.Services.AddHostedService<RulesEngineWorker>();

var host = builder.Build();
host.Run();