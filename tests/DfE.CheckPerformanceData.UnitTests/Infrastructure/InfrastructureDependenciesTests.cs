using DfE.CheckPerformanceData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// Guards the registration bundle the rules-engine worker builds its container from.
//
// The worker is the only host that calls AddInfrastructureDependencies. The web host does not —
// it assembles its own set in AddCpdBlobStorage — so a service added to this bundle for the web's
// benefit is executed only by the worker. If its dependencies are not also in the worker's
// container, validate-on-build kills the host: the queue consumer, the dead-letter, metrics,
// search-analytics and content-staging retention jobs all stop, over a service none of them uses.
//
// That has now happened twice. The Build workflow cannot catch it, because it compiles and runs
// tests and never starts the worker, so the failure reaches whoever runs the container next.
public class InfrastructureDependenciesTests
{
    // Azurite's well-known development account. Nothing connects during registration or
    // validation, so no storage emulator has to be running for this test.
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    // Mirrors the registrations RulesEngineWorker/Program.cs makes before and after its call to
    // AddInfrastructureDependencies — the collaborators the bundle is entitled to assume. Keep in
    // step with that file: anything the worker stops registering has to come out of here too, or
    // this test vouches for a container the worker does not actually have.
    private static IServiceCollection WorkerServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // The blob clients in this bundle take a BlobServiceClient, which the bundle only
            // registers when a storage connection string is present. Compose and every deployed
            // environment supply one, so a test without it would be checking a container shape
            // no host ever has.
            ["ConnectionStrings:AzureStorage"] = AzuriteConnection,
            ["ZendeskSettings:Subdomain"] = "dfe",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:ClientId"] = "test-client-id",
            ["ZendeskSettings:ClientSecret"] = "test-client-secret",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureDependencies(config);
        return services;
    }

    // Constructing every registration is exactly what the host does at startup, and exactly what
    // the worker died doing. Asserting it here turns a crash-loop into a red test.
    [Fact]
    public void EveryRegistrationInTheBundle_CanBeConstructed()
    {
        var services = WorkerServices();

        var exception = Record.Exception(() => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        Assert.Null(exception);
    }
}
