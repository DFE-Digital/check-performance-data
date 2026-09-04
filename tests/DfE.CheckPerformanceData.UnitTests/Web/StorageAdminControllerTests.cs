using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class StorageAdminNavEntryTests
{
    // The browser deletes blobs, so it is a Danger zone tile — there is no storage group.
    [Fact]
    public void StorageBrowserNavEntry_HasCorrectKeyAndParent()
    {
        var entry = new StorageBrowserNavEntry();
        Assert.Equal("storage-browser", entry.Key);
        Assert.Equal("danger-zone", entry.ParentKey);
        Assert.Equal("/admin/storage", entry.Url);
        Assert.True(entry.Enabled);
    }
}

public sealed class StorageAdminControllerTests
{
    private static StorageAdminController BuildSut(
        IReadOnlyDictionary<string, BlobServiceClient> accounts,
        params string[] protectedContainers) =>
        new(accounts, Options.Create(new StorageBrowserOptions
        {
            ProtectedContainers = protectedContainers.Length > 0
                ? protectedContainers
                : new StorageBrowserOptions().ProtectedContainers
        }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static StorageAdminController BuildSut(string account, BlobServiceClient client) =>
        BuildSut(new Dictionary<string, BlobServiceClient> { [account] = client });

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

        var sut = BuildSut("app", blobServiceClient);

        var result = await sut.Delete("app", "my-container", "request_123.json");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/storage/app/my-container", redirect.Url);
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

        var sut = BuildSut("app", blobServiceClient);

        var result = await sut.Delete("app", "window-123", "draft_requests/abc.json");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/storage/app/window-123", redirect.Url);
    }

    [Fact]
    public async Task Delete_UnknownAccount_ReturnsNotFound()
    {
        var sut = BuildSut(new Dictionary<string, BlobServiceClient>());

        var result = await sut.Delete("unknown", "my-container", "blob.json");

        Assert.IsType<NotFoundResult>(result);
    }
}


// The Data Protection keyring lives in blob storage beside ordinary application data, and the
// browser reached it exactly as it reaches anything else: an administrator could read the key
// descriptors in the preview pane, download keys.xml, delete it — invalidating every session and
// antiforgery token at once — or upload a replacement and mint tokens at will. The section grant
// governs who may use the browser, not what the browser may touch, and this is the second gate.
//
// Every route is covered rather than just the two the assessment happened to exercise: six
// separate guards drift, and a container that is off-limits has to be off-limits for listing,
// reading, writing and deleting alike.
public sealed class StorageAdminProtectedContainerTests
{
    private const string Keyring = "data-protection-keys";

    private static StorageAdminController BuildSut(
        BlobServiceClient client, params string[] protectedContainers) =>
        new(new Dictionary<string, BlobServiceClient> { ["app"] = client },
            Options.Create(new StorageBrowserOptions
            {
                ProtectedContainers = protectedContainers.Length > 0
                    ? protectedContainers
                    : new StorageBrowserOptions().ProtectedContainers
            }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    // The keyring is protected out of the box. An environment that has to add another secret
    // container should not have to discover that the default was empty.
    [Fact]
    public void TheKeyringIsProtectedByDefault()
    {
        Assert.Contains(Keyring, new StorageBrowserOptions().ProtectedContainers);
    }


    // The container must not appear in the browser at all. Refusing the routes but still listing
    // it advertises where the keyring lives and invites someone to go looking for a way in.
    [Fact]
    public async Task Containers_DoesNotListProtectedContainers()
    {
        var service = Substitute.For<BlobServiceClient>();
        service.GetBlobContainersAsync(
                Arg.Any<BlobContainerTraits>(), Arg.Any<BlobContainerStates>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncPageable<BlobContainerItem>.FromPages([
                Page<BlobContainerItem>.FromValues([
                    BlobsModelFactory.BlobContainerItem("window-123", null),
                    BlobsModelFactory.BlobContainerItem(Keyring, null),
                    BlobsModelFactory.BlobContainerItem("rules-config", null),
                ], null, Substitute.For<Response>())]));

        var result = await BuildSut(service).Containers("app");

        var model = Assert.IsType<StorageContainerListViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.DoesNotContain(Keyring, model.Containers);
        Assert.Contains("window-123", model.Containers);
        Assert.Contains("rules-config", model.Containers);
    }

    [Fact]
    public async Task Preview_OfAProtectedContainer_Is404_AndReadsNothing()
    {
        var service = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        service.GetBlobContainerClient(Keyring).Returns(container);

        var result = await BuildSut(service).Preview("app", Keyring, "keys.xml");

        Assert.IsType<NotFoundResult>(result);
        // The refusal has to come before the blob is touched — a protected blob that is read and
        // then withheld has still been read.
        service.DidNotReceive().GetBlobContainerClient(Keyring);
    }

    [Fact]
    public async Task Download_OfAProtectedContainer_Is404_AndReadsNothing()
    {
        var service = Substitute.For<BlobServiceClient>();

        var result = await BuildSut(service).Download("app", Keyring, "keys.xml");

        Assert.IsType<NotFoundResult>(result);
        service.DidNotReceive().GetBlobContainerClient(Keyring);
    }

    // Deleting the keyring is not a leak but an outage: every issued cookie and antiforgery token
    // becomes undecryptable at once.
    [Fact]
    public async Task Delete_InAProtectedContainer_Is404_AndDeletesNothing()
    {
        var service = Substitute.For<BlobServiceClient>();

        var result = await BuildSut(service).Delete("app", Keyring, "keys.xml");

        Assert.IsType<NotFoundResult>(result);
        service.DidNotReceive().GetBlobContainerClient(Keyring);
    }

    // Worse than reading it: a replacement keyring lets tokens be forged rather than merely read.
    [Fact]
    public async Task Upload_ToAProtectedContainer_Is404_AndWritesNothing()
    {
        var service = Substitute.For<BlobServiceClient>();

        var result = await BuildSut(service).Upload("app", Keyring, [], null, null);

        Assert.IsType<NotFoundResult>(result);
        service.DidNotReceive().GetBlobContainerClient(Keyring);
    }

    [Fact]
    public async Task Container_ForAProtectedContainer_Is404()
    {
        var service = Substitute.For<BlobServiceClient>();

        var result = await BuildSut(service).Container("app", Keyring, null);

        Assert.IsType<NotFoundResult>(result);
        service.DidNotReceive().GetBlobContainerClient(Keyring);
    }

    // Blob container names are lower-case by Azure's rules, but the guard compares strings and a
    // configured entry could be typed in any case.
    [Theory]
    [InlineData("Data-Protection-Keys")]
    [InlineData("DATA-PROTECTION-KEYS")]
    public async Task ProtectedMatching_IgnoresCase(string requested)
    {
        var service = Substitute.For<BlobServiceClient>();

        Assert.IsType<NotFoundResult>(await BuildSut(service).Download("app", requested, "keys.xml"));
    }

    // The deny-list is configuration so a future secret container is covered without a code change.
    [Fact]
    public async Task AConfiguredContainer_IsProtectedToo()
    {
        var service = Substitute.For<BlobServiceClient>();

        var result = await BuildSut(service, "secrets-vault").Download("app", "secrets-vault", "x.txt");

        Assert.IsType<NotFoundResult>(result);
    }

    // The guard must not turn the browser off. An ordinary container still resolves, and the
    // unknown-account 404 must keep coming from the account check rather than the new one.
    [Fact]
    public async Task AnOrdinaryContainer_IsStillReachable()
    {
        var service = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        service.GetBlobContainerClient("window-123").Returns(container);
        container.GetBlobClient("draft.json").Returns(blob);
        blob.DeleteIfExistsAsync(Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>()).Returns(Response.FromValue(true, Substitute.For<Response>()));

        var result = await BuildSut(service).Delete("app", "window-123", "draft.json");

        Assert.IsType<RedirectResult>(result);
    }
}
