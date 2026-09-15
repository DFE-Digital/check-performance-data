using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// S7: a create-only upload's 409 must be interpreted narrowly — only the conflict the code
// actually handles (a blob already exists) is worth its own exception; anything else (a lease, a
// container mid-delete) must propagate as-is rather than being misreported as "remove it by hand".
public sealed class EgressBlobClientTests
{
    private static readonly EgressStorageOptions Options = new() { Container = "cypmd", Prefix = "extracts_input/" };

    private static (EgressBlobClient Sut, BlobClient Blob) Build()
    {
        var service = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        service.GetBlobContainerClient("cypmd").Returns(container);
        container.CreateIfNotExistsAsync(
            Arg.Any<PublicAccessType>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<BlobContainerEncryptionScopeOptions>(), Arg.Any<CancellationToken>())
            .Returns((Response<BlobContainerInfo>?)null);
        container.GetBlobClient("extracts_input/CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv").Returns(blob);
        var sut = new EgressBlobClient(new Dictionary<string, BlobServiceClient> { ["egress"] = service }, Microsoft.Extensions.Options.Options.Create(Options));
        return (sut, blob);
    }

    [Fact]
    public async Task A_409_for_an_existing_blob_becomes_the_named_exception()
    {
        var (sut, blob) = Build();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<BlobContentInfo>>(new RequestFailedException(409, "already exists", nameof(BlobErrorCode.BlobAlreadyExists), null)));

        await Assert.ThrowsAsync<EgressBlobAlreadyExistsException>(
            () => sut.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", [1, 2, 3], "sha", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_409_for_any_other_reason_propagates_unchanged()
    {
        var (sut, blob) = Build();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<BlobContentInfo>>(new RequestFailedException(409, "container is being deleted", "ContainerBeingDeleted", null)));

        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => sut.UploadAsync("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", [1, 2, 3], "sha", Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("ContainerBeingDeleted", ex.ErrorCode);
    }
}
