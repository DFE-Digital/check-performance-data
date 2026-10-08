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
// (uncommitted) blocks under the blob name. Neither listing may show that name, and Preview reads
// only the start of a large blob. Real blob semantics (Azurite).
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

    [Fact]
    public async Task Preview_OfASmallBlob_ShowsAllOfIt_AndIsNotPartial()
    {
        var container = await NewContainer();
        await container.GetBlobClient("small.json").UploadAsync(new BinaryData("{\"a\":1}"),
            new Azure.Storage.Blobs.Models.BlobUploadOptions { HttpHeaders = new() { ContentType = "application/json" } });

        var model = Assert.IsType<StorageBlobPreviewViewModel>(
            Assert.IsType<ViewResult>(await Sut().Preview("app", container.Name, "small.json")).Model);

        Assert.False(model.IsPartial);
        Assert.Contains("\"a\": 1", model.Content); // pretty-printed because it was read whole
    }

    [Fact]
    public async Task Preview_OfAnEmptyBlob_IsEmptyAndNotPartial()
    {
        var container = await NewContainer();
        await container.GetBlobClient("empty.txt").UploadAsync(new BinaryData(""),
            new Azure.Storage.Blobs.Models.BlobUploadOptions { HttpHeaders = new() { ContentType = "text/plain" } });

        var model = Assert.IsType<StorageBlobPreviewViewModel>(
            Assert.IsType<ViewResult>(await Sut().Preview("app", container.Name, "empty.txt")).Model);

        Assert.False(model.IsPartial);
        Assert.Equal(string.Empty, model.Content);
    }

    [Fact]
    public async Task Preview_OfABlobOverTheCap_ShowsOnlyTheFirst256KB_AndSaysSo()
    {
        var container = await NewContainer();
        // "é" is two bytes, so the 262,144-byte cut falls on an odd offset when a leading "a" shifts it.
        var text = "a" + string.Concat(Enumerable.Repeat("é", 200_000));
        await container.GetBlobClient("big.txt").UploadAsync(new BinaryData(text),
            new Azure.Storage.Blobs.Models.BlobUploadOptions { HttpHeaders = new() { ContentType = "text/plain" } });

        var model = Assert.IsType<StorageBlobPreviewViewModel>(
            Assert.IsType<ViewResult>(await Sut().Preview("app", container.Name, "big.txt")).Model);

        Assert.True(model.IsPartial);
        Assert.NotNull(model.Content);
        Assert.DoesNotContain('�', model.Content);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(model.Content);
        Assert.InRange(bytes, 262_144 - 3, 262_144);
    }

    [Fact]
    public async Task Preview_OfAPartialJsonBlob_ShowsTheRawText()
    {
        var container = await NewContainer();
        var json = "[" + string.Join(",", Enumerable.Repeat("{\"a\":1}", 60_000)) + "]";
        await container.GetBlobClient("big.json").UploadAsync(new BinaryData(json),
            new Azure.Storage.Blobs.Models.BlobUploadOptions { HttpHeaders = new() { ContentType = "application/json" } });

        var model = Assert.IsType<StorageBlobPreviewViewModel>(
            Assert.IsType<ViewResult>(await Sut().Preview("app", container.Name, "big.json")).Model);

        Assert.True(model.IsPartial);
        Assert.StartsWith("[{\"a\":1},{\"a\":1}", model.Content); // not re-indented
    }
}
