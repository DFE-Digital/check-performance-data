using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class BlobStorageExtensions
{
    public static IServiceCollection AddCpdBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
            new BlobServiceClient(configuration.GetConnectionString("AzureStorage")));

        services.AddSingleton<IReadOnlyDictionary<string, BlobServiceClient>>(_ =>
        {
            var clients = new Dictionary<string, BlobServiceClient>();
            var appConn = configuration.GetConnectionString("AzureStorage");
            if (!string.IsNullOrEmpty(appConn))
                clients["app"] = new BlobServiceClient(appConn);
            var ingressConn = configuration.GetConnectionString("IngressStorage");
            if (!string.IsNullOrEmpty(ingressConn))
                clients["ingress"] = new BlobServiceClient(ingressConn);
            return clients;
        });
        services.Configure<DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions>(
            configuration.GetSection(
                DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<
            DfE.CheckPerformanceData.Application.RulesConfig.IRulesConfigStore,
            DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesConfigStore>();
        // Lets the dev-data seeding orchestrator self-seed the rules-config blobs in dev/E2E. In
        // deployed environments the rules-engine worker seeds them; that worker isn't part of the
        // local web stack, so without this the admin rules editor 404s on a fresh environment.
        services.AddScoped<DfE.CheckPerformanceData.Infrastructure.RulesEngine.RulesConfigSeeder>();
        // TODO: revert to QuestionFlowBlobClient once storage permissions are configured for deployed environments
        //if (builder.Environment.IsDevelopment())
            services.AddSingleton<IQuestionFlowBlobClient, QuestionFlowBlobClient>();
        // else
        //     builder.Services.AddSingleton<IQuestionFlowBlobClient>(_ =>
        //         new FileSystemQuestionFlowClient(builder.Environment.ContentRootPath));
        services.AddScoped<IRequestBlobClient, RequestBlobClient>();
        services.AddScoped<IRequestStateBlobClient, RequestStateBlobClient>();
        services.AddScoped<IPupilDataBlobClient, PupilDataBlobClient>();
        services.AddScoped<ICsvSchemaFileProcessor, CsvSchemaFileProcessor>();

        return services;
    }
}
