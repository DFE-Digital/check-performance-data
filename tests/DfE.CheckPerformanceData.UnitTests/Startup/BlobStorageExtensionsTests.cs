using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.QuestionFlow;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

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
        // The question-flow source reads the shipped JSON from the content root, so the host
        // environment has to be resolvable here exactly as it is in the real web host.
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddCpdBlobStorage(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "DfE.CheckPerformanceData.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    [Theory]
    [InlineData(typeof(IPupilDataBlobClient))]
    [InlineData(typeof(IStudentResultsClient))]
    [InlineData(typeof(IGradeReferenceClient))]
    [InlineData(typeof(GradeReferenceBlobClient))]
    // AB#297848: the qualification reference, resolved the same way for the same reason.
    [InlineData(typeof(IQualificationReferenceClient))]
    [InlineData(typeof(QualificationReferenceBlobClient))]
    [InlineData(typeof(IRequestBlobClient))]
    [InlineData(typeof(IRequestStateBlobClient))]
    [InlineData(typeof(IQuestionFlowConfigSource))]
    public void Every_blob_client_the_web_host_needs_resolves(Type serviceType)
    {
        using var provider = BuildWebBlobServices();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    /// <summary>
    /// Question flow configs ship inside the release image (Data/QuestionFlows/*.json) and are
    /// read from there in every environment. They used to be read from the question-flows blob
    /// container, which Terraform provisions empty and which only the dev-only seeding step ever
    /// filled — so QA, preproduction and production had no configs unless somebody uploaded them
    /// by hand. Reading the image makes the config travel with the release it belongs to.
    /// </summary>
    [Fact]
    public void Question_flows_are_served_from_the_shipped_image_not_from_blob()
    {
        using var provider = BuildWebBlobServices();
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IQuestionFlowConfigSource>();

        Assert.IsType<FileSystemQuestionFlowClient>(source);
    }
}
