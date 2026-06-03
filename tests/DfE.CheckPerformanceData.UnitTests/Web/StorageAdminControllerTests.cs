using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class StorageAdminNavEntryTests
{
    [Fact]
    public void StorageAdminGroupNavEntry_HasCorrectKeyAndIsGroup()
    {
        var entry = new StorageAdminGroupNavEntry();
        Assert.Equal("storage-admin", entry.Key);
        Assert.Null(entry.ParentKey);
        Assert.True(entry.Enabled);
        Assert.Equal(30, entry.Order);
    }

    [Fact]
    public void StorageBrowserNavEntry_HasCorrectKeyAndParent()
    {
        var entry = new StorageBrowserNavEntry();
        Assert.Equal("storage-browser", entry.Key);
        Assert.Equal("storage-admin", entry.ParentKey);
        Assert.Equal("/admin/storage", entry.Url);
        Assert.True(entry.Enabled);
    }
}

public sealed class StorageAdminControllerTests
{
    private static StorageAdminController BuildSut(BlobServiceClient client) =>
        new(client)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    [Fact]
    public async Task Delete_DeletesBlob_AndRedirectsToContainerView()
    {
        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var containerClient = Substitute.For<BlobContainerClient>();
        var blobClient = Substitute.For<BlobClient>();

        blobServiceClient.GetBlobContainerClient("my-container").Returns(containerClient);
        containerClient.GetBlobClient("request_123.json").Returns(blobClient);
        blobClient.DeleteIfExistsAsync(
            Arg.Any<DeleteSnapshotsOption>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));

        var sut = BuildSut(blobServiceClient);

        var result = await sut.Delete("my-container", "request_123.json");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/storage/my-container", redirect.Url);
    }

    [Fact]
    public async Task Delete_BlobNameWithSlashes_RedirectsToContainer()
    {
        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var containerClient = Substitute.For<BlobContainerClient>();
        var blobClient = Substitute.For<BlobClient>();

        blobServiceClient.GetBlobContainerClient("window-123").Returns(containerClient);
        containerClient.GetBlobClient("draft_requests/abc.json").Returns(blobClient);
        blobClient.DeleteIfExistsAsync(
            Arg.Any<DeleteSnapshotsOption>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));

        var sut = BuildSut(blobServiceClient);

        var result = await sut.Delete("window-123", "draft_requests/abc.json");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/storage/window-123", redirect.Url);
    }
}
