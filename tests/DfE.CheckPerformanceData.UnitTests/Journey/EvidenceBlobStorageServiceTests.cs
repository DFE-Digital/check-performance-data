using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class EvidenceBlobStorageServiceTests
{
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly EvidenceBlobStorageService _sut;

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] AnyBytes = [1, 2, 3];

    public EvidenceBlobStorageServiceTests()
    {
        _blobServiceClient.GetBlobContainerClient(WindowId.ToString()).Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        _sut = new EvidenceBlobStorageService(_blobServiceClient);
    }

    // ── SaveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_UsesWindowIdAsContainerName()
    {
        await _sut.SaveAsync(WindowId, AnyBytes);

        _blobServiceClient.Received(1).GetBlobContainerClient(WindowId.ToString());
    }

    [Fact]
    public async Task SaveAsync_StoresBlobUnderEvidenceFolder()
    {
        await _sut.SaveAsync(WindowId, AnyBytes);

        _containerClient.Received(1).GetBlobClient(Arg.Is<string>(s => s.StartsWith("Evidence/")));
    }

    [Fact]
    public async Task SaveAsync_ReturnsGuidBlobName()
    {
        var result = await _sut.SaveAsync(WindowId, AnyBytes);

        Assert.True(Guid.TryParse(result, out _));
    }

    [Fact]
    public async Task SaveAsync_ReturnedNameMatchesBlobPath()
    {
        var result = await _sut.SaveAsync(WindowId, AnyBytes);

        _containerClient.Received(1).GetBlobClient($"Evidence/{result}");
    }

    [Fact]
    public async Task SaveAsync_EachCallReturnsDistinctName()
    {
        var first = await _sut.SaveAsync(WindowId, AnyBytes);
        var second = await _sut.SaveAsync(WindowId, AnyBytes);

        Assert.NotEqual(first, second);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_UsesWindowIdAsContainerName()
    {
        var blobName = Guid.NewGuid().ToString();

        await _sut.DeleteAsync(WindowId, blobName);

        _blobServiceClient.Received(1).GetBlobContainerClient(WindowId.ToString());
    }

    [Fact]
    public async Task DeleteAsync_DeletesBlobFromEvidenceFolder()
    {
        var blobName = Guid.NewGuid().ToString();

        await _sut.DeleteAsync(WindowId, blobName);

        _containerClient.Received(1).GetBlobClient($"Evidence/{blobName}");
    }
}
