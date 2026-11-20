using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The media file store against real blob semantics (Azurite). Each test uses its own container so
// runs stay isolated; production uses cms-media.
[Collection(nameof(AzuriteCollection))]
public sealed class MediaBlobStoreTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private MediaBlobStore NewStore(out string container)
    {
        container = $"media-{Guid.NewGuid():N}";
        return new MediaBlobStore(_blobs, container);
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ProductionContainer_IsCmsMedia()
    {
        Assert.Equal("cms-media", MediaBlobKeys.ContainerName);
    }

    [Fact]
    public async Task Put_CreatesTheContainerIfMissing_AndGetReturnsTheBytesAndType()
    {
        var sut = NewStore(out var container);
        var key = MediaBlobKeys.Original(Guid.NewGuid(), 1);

        Assert.True(await sut.PutAsync(key, Bytes("png-bytes"), "image/png"));

        Assert.True(await _blobs.GetBlobContainerClient(container).ExistsAsync());
        await using var blob = await sut.GetAsync(key);
        Assert.NotNull(blob);
        Assert.Equal("image/png", blob!.ContentType);
        Assert.Equal(9, blob.Length);
        using var reader = new StreamReader(blob.Content);
        Assert.Equal("png-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Put_NeverOverwritesAStoredFile()
    {
        var sut = NewStore(out _);
        var key = MediaBlobKeys.Rendition(Guid.NewGuid(), 1, "abc123");

        Assert.True(await sut.PutAsync(key, Bytes("first"), "image/webp"));
        Assert.False(await sut.PutAsync(key, Bytes("second"), "image/png"));

        await using var blob = await sut.GetAsync(key);
        using var reader = new StreamReader(blob!.Content);
        Assert.Equal("first", await reader.ReadToEndAsync());
        Assert.Equal("image/webp", blob.ContentType);
    }

    [Fact]
    public async Task ExistsAndDelete_ReportWhatWasThere()
    {
        var sut = NewStore(out _);
        var key = MediaBlobKeys.Original(Guid.NewGuid(), 2);

        Assert.False(await sut.ExistsAsync(key));
        await sut.PutAsync(key, Bytes("x"), "image/gif");
        Assert.True(await sut.ExistsAsync(key));

        Assert.True(await sut.DeleteAsync(key));
        Assert.False(await sut.ExistsAsync(key));
        Assert.False(await sut.DeleteAsync(key));
    }

    // Reads never create the container and never throw for a missing file or container.
    [Fact]
    public async Task Reads_OnAMissingContainer_ReturnNothing()
    {
        var sut = NewStore(out var container);
        var key = MediaBlobKeys.Original(Guid.NewGuid(), 1);

        Assert.Null(await sut.GetAsync(key));
        Assert.False(await sut.ExistsAsync(key));
        Assert.False(await sut.DeleteAsync(key));
        Assert.False(await _blobs.GetBlobContainerClient(container).ExistsAsync());
    }
}
