using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// Guards the web host's blob-client registrations.
//
// The web host does NOT call AddInfrastructureDependencies — it builds its own blob-client set in
// AddCpdBlobStorage. So a client registered only in the Infrastructure DependencyManager resolves
// in the worker and in tests but throws at web startup, and because the container is validated on
// build the whole app crashloops rather than failing on one page. AB#296648 hit exactly that: the
// dev-data seeding orchestrator took IStudentResultsClient and the app would not boot. These
// assertions turn that class of failure back into a test failure.
public class BlobStorageExtensionsTests
{
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private static ServiceProvider BuildWebBlobServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AzureStorage"] = AzuriteConnection
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddCpdBlobStorage(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Theory]
    [InlineData(typeof(IPupilDataBlobClient))]
    [InlineData(typeof(IStudentResultsClient))]
    [InlineData(typeof(IGradeReferenceClient))]
    [InlineData(typeof(GradeReferenceBlobClient))]
    [InlineData(typeof(IRequestBlobClient))]
    [InlineData(typeof(IRequestStateBlobClient))]
    [InlineData(typeof(IQuestionFlowBlobClient))]
    public void Every_blob_client_the_web_host_needs_resolves(Type serviceType)
    {
        using var provider = BuildWebBlobServices();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }
}
