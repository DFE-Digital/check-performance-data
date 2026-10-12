using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Web.Admin;

namespace DfE.CheckPerformanceData.IntegrationTests.StorageBrowser;

// #568: the stage/commit helper against real block-blob semantics.
[Collection(nameof(AzuriteCollection))]
public sealed class StorageChunkedUploadTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private async Task<BlockBlobClient> NewBlobAsync()
    {
        var container = _blobs.GetBlobContainerClient($"chunked-{Guid.NewGuid():N}");
        await container.CreateAsync();
        return container.GetBlockBlobClient("reports/big.csv");
    }

    private static MemoryStream Part(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task StagedParts_CommitInOrder_WithTheContentType()
    {
        var blob = await NewBlobAsync();
        var uploadId = Guid.NewGuid();
        await StorageChunkedUpload.StageAsync(blob, uploadId, 2, Part("CC"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, uploadId, 0, Part("AA"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, uploadId, 1, Part("BB"), CancellationToken.None);

        var outcome = await StorageChunkedUpload.CommitAsync(blob, uploadId, 3, maxBytes: 100, "text/csv", CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.Committed, outcome.Status);
        Assert.Equal(6, outcome.Bytes);
        var downloaded = await blob.DownloadContentAsync();
        Assert.Equal("AABBCC", downloaded.Value.Content.ToString());
        Assert.Equal("text/csv", (await blob.GetPropertiesAsync()).Value.ContentType);
    }

    [Fact]
    public async Task Commit_IsRepeatable_AndLeavesTheBlobUnchanged()
    {
        var blob = await NewBlobAsync();
        var uploadId = Guid.NewGuid();
        await StorageChunkedUpload.StageAsync(blob, uploadId, 0, Part("AA"), CancellationToken.None);
        await StorageChunkedUpload.CommitAsync(blob, uploadId, 1, 100, "text/csv", CancellationToken.None);

        var again = await StorageChunkedUpload.CommitAsync(blob, uploadId, 1, 100, "text/csv", CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.Committed, again.Status);
        Assert.Equal("AA", (await blob.DownloadContentAsync()).Value.Content.ToString());
    }

    [Fact]
    public async Task Commit_WithAMissingPart_IsIncomplete_AndCreatesNoBlob()
    {
        var blob = await NewBlobAsync();
        var uploadId = Guid.NewGuid();
        await StorageChunkedUpload.StageAsync(blob, uploadId, 0, Part("AA"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, uploadId, 2, Part("CC"), CancellationToken.None);

        var outcome = await StorageChunkedUpload.CommitAsync(blob, uploadId, 3, 100, null, CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.Incomplete, outcome.Status);
        Assert.False(await blob.ExistsAsync());
    }

    [Fact]
    public async Task Commit_WithNothingStaged_IsIncomplete()
    {
        var blob = await NewBlobAsync();

        var outcome = await StorageChunkedUpload.CommitAsync(blob, Guid.NewGuid(), 1, 100, null, CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.Incomplete, outcome.Status);
        Assert.False(await blob.ExistsAsync());
    }

    // The maximum is enforced from what storage holds, not from a declared total.
    [Fact]
    public async Task Commit_OverTheMaximum_IsTooLarge_AndCreatesNoBlob()
    {
        var blob = await NewBlobAsync();
        var uploadId = Guid.NewGuid();
        await StorageChunkedUpload.StageAsync(blob, uploadId, 0, Part("AAAA"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, uploadId, 1, Part("BBBB"), CancellationToken.None);

        var outcome = await StorageChunkedUpload.CommitAsync(blob, uploadId, 2, maxBytes: 7, null, CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.TooLarge, outcome.Status);
        Assert.Equal(8, outcome.Bytes);
        Assert.False(await blob.ExistsAsync());
    }

    // Blocks are scoped to the blob: a commit only ever sees blocks staged under the same name
    // and the same upload id, so one upload cannot borrow another's parts.
    [Fact]
    public async Task Commit_IgnoresBlocksOfAnotherUpload_ToTheSameName()
    {
        var blob = await NewBlobAsync();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await StorageChunkedUpload.StageAsync(blob, theirs, 0, Part("XX"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, theirs, 1, Part("YY"), CancellationToken.None);
        await StorageChunkedUpload.StageAsync(blob, mine, 0, Part("AA"), CancellationToken.None);

        var outcome = await StorageChunkedUpload.CommitAsync(blob, mine, 2, 100, null, CancellationToken.None);

        Assert.Equal(ChunkedCommitStatus.Incomplete, outcome.Status);
        Assert.False(await blob.ExistsAsync());
    }
}
