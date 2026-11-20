using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// The upload service lives in Application and its Skia image cleaner in Infrastructure, and the
// web host never calls AddInfrastructureDependencies. This proves the web registrations alone
// give a working upload service.
public sealed class MediaUploadRegistrationTests
{
    [Fact]
    public void TheWebHostResolvesTheUploadService_WithTheSkiaCleaner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AzureStorage"] = "UseDevelopmentStorage=true" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton(Substitute.For<IMediaAssetRepository>());
        services.AddApplicationDependencies();
        services.AddCpdBlobStorage(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<MediaUploadService>(scope.ServiceProvider.GetRequiredService<IMediaUploadService>());
        Assert.IsType<SkiaRasterImageCleaner>(scope.ServiceProvider.GetRequiredService<IRasterImageCleaner>());
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "DfE.CheckPerformanceData.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
