using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.StorageBrowser;

// #568 final review: a chunked upload that was cancelled or is still running leaves staged
// (uncommitted) blocks under the blob name. Neither listing may show that name. Real blob
// semantics (Azurite).
[Collection(nameof(AzuriteCollection))]
public sealed class StorageBrowserListingAndPreviewTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private StorageAdminController Sut() =>
        new(new Dictionary<string, BlobServiceClient> { ["app"] = _blobs },
            Options.Create(new StorageBrowserOptions()), NullLogger<StorageAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private async Task<BlobContainerClient> NewContainer()
    {
        var name = $"list-{Guid.NewGuid():N}";
        await _blobs.CreateBlobContainerAsync(name);
        return _blobs.GetBlobContainerClient(name);
    }

    private static async Task StageOnly(BlobContainerClient container, string name)
    {
        var block = container.GetBlockBlobClient(name);
        await StorageChunkedUpload.StageAsync(block, Guid.NewGuid(), 0, new MemoryStream([1, 2, 3]), default);
    }

    [Fact]
    public async Task Container_DoesNotListABlobThatOnlyHasStagedBlocks()
    {
        var container = await NewContainer();
        await StageOnly(container, "ghost.csv");
        await container.GetBlobClient("real.csv").UploadAsync(new BinaryData("a,b"));

        var result = await Sut().Container("app", container.Name, null);

        var model = Assert.IsType<StorageBlobListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(["real.csv"], model.Blobs.Select(b => b.Name));
    }

    [Fact]
    public async Task IngressBrowse_DoesNotOfferABlobThatOnlyHasStagedBlocks()
    {
        var container = await NewContainer();
        await StageOnly(container, "ghost.csv");
        await container.GetBlobClient("real.csv").UploadAsync(new BinaryData("a,b"));
        var sut = new IngressFileController(NullLogger<IngressFileController>.Instance,
            Substitute.For<IWindowService>(), new Dictionary<string, BlobServiceClient> { ["ingress"] = _blobs });

        var result = await sut.Browse(Guid.NewGuid(), CheckingExerciseType.PupilData, "ks4", container.Name, null, default);

        var model = Assert.IsType<IngressFolderBrowseViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(["real.csv"], model.Files);
    }
}
