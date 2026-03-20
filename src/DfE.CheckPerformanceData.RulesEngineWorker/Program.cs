using Azure.Storage.Queues;
using DfE.CheckPerformanceData.RulesEngineWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RulesEngineOptions>(builder.Configuration.GetSection("RulesEngineOptions"));

var conn = builder.Configuration.GetConnectionString("AzureStorage");
Console.WriteLine(conn);


// builder.Services.AddSingleton(sp => new QueueServiceClient("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=http://localhost:10001/devstoreaccount1;",
//     new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05){
//         MessageEncoding = QueueMessageEncoding.Base64
//     }));

builder.Services.AddSingleton(sp => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
    new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05){
        MessageEncoding = QueueMessageEncoding.Base64
    }));

builder.Services.AddHostedService<RulesEngineWorker>();

var host = builder.Build();
host.Run();