using Azure.Storage.Queues;
using DfE.CheckPerformanceData.RulesEngineWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RulesEngineOptions>(builder.Configuration.GetSection("RulesEngineOptions"));

builder.Services.AddSingleton(sp => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
    new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05){
        MessageEncoding = QueueMessageEncoding.Base64
    }));

builder.Services.AddHostedService<RulesEngineWorker>();

var host = builder.Build();
host.Run();