using System;
using System.Collections.Generic;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.QuestionFlow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class BlobStorageExtensions
{
    public static IServiceCollection AddCpdBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
            new BlobServiceClient(configuration.GetConnectionString("AzureStorage")));

        // The storage browser's deny-list. Bound rather than hard-coded so an environment can add
        // a secret container without a release; the built-in default already covers the keyring,
        // so an environment that binds nothing is still protected.
        services.Configure<StorageBrowserOptions>(
            configuration.GetSection(StorageBrowserOptions.SectionName));

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
        services.Configure<Infrastructure.RulesEngine.BlobRulesProviderOptions>(
            configuration.GetSection(
                Infrastructure.RulesEngine.BlobRulesProviderOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<
            Application.RulesConfig.IRulesConfigStore,
            Infrastructure.RulesEngine.BlobRulesConfigStore>();
        // Lets the dev-data seeding orchestrator self-seed the rules-config blobs in dev/E2E. In
        // deployed environments the rules-engine worker seeds them; that worker isn't part of the
        // local web stack, so without this the admin rules editor 404s on a fresh environment.
        services.AddScoped<Infrastructure.RulesEngine.RulesConfigSeeder>();
        // Question flow configs are read from the release image (Data/QuestionFlows/*.json), not
        // from storage, in every environment. They used to live in the question-flows blob
        // container, which Terraform provisions empty and which only the Development-gated
        // seeding step ever filled — so QA, preproduction and production held whatever had been
        // uploaded by hand, if anything, and a config change did not reach an environment just by
        // deploying. The JSON already ships in the image, so reading it there makes a flow
        // travel with the release that contains it and removes the drift entirely.
        //
        // The trade is that a flow cannot be hotfixed without a redeploy. Nothing edits these at
        // runtime — there is no admin flow editor — so that costs nothing today. Reinstating a
        // storage-backed source means reinstating a seeder that overwrites on every startup,
        // version-gated like RulesConfigSeeder; see docs/question-flow-deployment.md.
        services.AddSingleton<IQuestionFlowConfigSource>(sp =>
            new FileSystemQuestionFlowClient(
                sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddScoped<IRequestBlobClient, RequestBlobClient>();
        services.AddScoped<IRequestStateBlobClient, RequestStateBlobClient>();
        services.AddScoped<IPupilDataBlobClient, PupilDataBlobClient>();
        // AB#296648: the 16-19 exam results the incorrect-grade enquiry journey reads. Registered
        // here as well as in the Infrastructure DependencyManager because the web host builds its
        // blob-client set from this seam and never calls AddInfrastructureDependencies.
        services.AddScoped<Application.ResultsEnquiry.IStudentResultsClient, StudentResultsBlobClient>();
        // AB#297130: the AODC grade reference lives beside rules.json in the rules-config container.
        // The concrete type is registered too, because the startup seeder needs SeedIfMissingAsync
        // (a seeding concern that has no business on the read interface).
        services.AddScoped<GradeReferenceBlobClient>();
        services.AddScoped<Application.ResultsEnquiry.IGradeReferenceClient>(
            sp => sp.GetRequiredService<GradeReferenceBlobClient>());
        services.AddHostedService<Seeding.GradeReferenceSeedingService>();
        // AB#297848: the QualList qualification reference, beside the grade reference. The concrete
        // type is registered too, for the same SeedIfMissingAsync reason as GradeReferenceBlobClient.
        services.AddScoped<QualificationReferenceBlobClient>();
        services.AddScoped<Application.ResultsEnquiry.IQualificationReferenceClient>(
            sp => sp.GetRequiredService<QualificationReferenceBlobClient>());
        services.AddHostedService<Seeding.QualificationReferenceSeedingService>();
        services.AddScoped<ICsvSchemaFileProcessor, CsvSchemaFileProcessor>();

        return services;
    }
}
