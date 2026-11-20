using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// What the public media endpoint sends back for each request: the stored file, a resized copy
// (made once, then read from storage) or nothing. A resize or crop that the server did not sign
// is refused before any image is read.
public sealed class MediaDeliveryServiceTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly byte[] OriginalBytes = [1, 2, 3];
    private static readonly byte[] RenderedBytes = [7, 7];

    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaBlobStore _blobs = Substitute.For<IMediaBlobStore>();
    private readonly IMediaRenditionRenderer _renderer = Substitute.For<IMediaRenditionRenderer>();
    private readonly MediaUrlSigner _signer = new(new FixedSigningKey(Enumerable.Repeat((byte)3, 32).ToArray()));
    private readonly MediaDeliveryService _sut;

    public MediaDeliveryServiceTests()
    {
        _sut = new MediaDeliveryService(_assets, _blobs, _renderer,
            new MediaProcessingGate(Options.Create(new MediaProcessingOptions())), _signer,
            NullLogger<MediaDeliveryService>.Instance);
        UseVersion(Version());
        _blobs.GetAsync(MediaBlobKeys.Original(Id, 1), Arg.Any<CancellationToken>())
            .Returns(_ => new MediaBlob(new MemoryStream(OriginalBytes), "image/jpeg", OriginalBytes.Length));
        _blobs.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _renderer.Render(Arg.Any<byte[]>(), Arg.Any<MediaRenditionLayout>()).Returns(RenderedBytes);
    }

    private static MediaAssetVersionDto Version(string contentType = "image/jpeg", bool animated = false, int? width = 2000, int? height = 1000) =>
        new(Id, 1, MediaBlobKeys.Original(Id, 1), contentType, width, height, OriginalBytes.Length, "sha", animated, DateTime.UtcNow, "u");

    private void UseVersion(MediaAssetVersionDto version) =>
        _assets.GetVersionAsync(Id, 1, Arg.Any<CancellationToken>()).Returns(version);

    private static List<KeyValuePair<string, string?>> Query(params (string Key, string? Value)[] pairs) =>
        pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)).ToList();

    private List<KeyValuePair<string, string?>> Signed(MediaRendition rendition)
    {
        var query = Query(("w", rendition.Width?.ToString()));
        if (rendition.Crop is { } c) query.Add(new("crop", $"{c.X},{c.Y},{c.Width},{c.Height}"));
        query.Add(new("sig", _signer.Sign(Id, 1, rendition)));
        return query;
    }

    private static async Task<byte[]> BytesAsync(MediaDelivery delivery)
    {
        await using var content = delivery.Content!;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        return copy.ToArray();
    }

    private string RenditionKey(MediaRendition rendition) =>
        MediaBlobKeys.Rendition(Id, 1, MediaRenditionQuery.Hash(rendition));

    [Fact]
    public async Task NoSettings_ServesTheStoredFile()
    {
        var result = await _sut.GetAsync(Id, 1, Query());

        Assert.Equal(MediaDeliveryOutcome.Found, result.Outcome);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(OriginalBytes, await BytesAsync(result));
        Assert.Equal($"\"{Id:N}.1\"", result.ETag);
        _renderer.DidNotReceiveWithAnyArgs().Render(default!, default!);
    }

    [Fact]
    public async Task TheContentType_ComesFromTheVersionRecord_NotTheBlob()
    {
        UseVersion(Version("image/png"));

        var result = await _sut.GetAsync(Id, 1, Query());

        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public async Task AnUnknownImageOrVersion_IsNotFound()
    {
        Assert.Equal(MediaDeliveryOutcome.NotFound, (await _sut.GetAsync(Guid.NewGuid(), 1, Query())).Outcome);
        Assert.Equal(MediaDeliveryOutcome.NotFound, (await _sut.GetAsync(Id, 2, Query())).Outcome);
    }

    [Fact]
    public async Task ARecordWhoseFileIsMissing_IsNotFound()
    {
        _blobs.GetAsync(MediaBlobKeys.Original(Id, 1), Arg.Any<CancellationToken>()).Returns((MediaBlob?)null);

        Assert.Equal(MediaDeliveryOutcome.NotFound, (await _sut.GetAsync(Id, 1, Query())).Outcome);
    }

    [Fact]
    public async Task AWidthWithNoSignature_IsRefused_BeforeAnythingIsRead()
    {
        var result = await _sut.GetAsync(Id, 1, Query(("w", "640")));

        Assert.Equal(MediaDeliveryOutcome.Invalid, result.Outcome);
        await _assets.DidNotReceiveWithAnyArgs().GetVersionAsync(default, default, default);
        _renderer.DidNotReceiveWithAnyArgs().Render(default!, default!);
    }

    [Fact]
    public async Task ATamperedWidth_IsRefused()
    {
        var query = Signed(new MediaRendition(320, null, null, null));
        query[0] = new("w", "1920");

        Assert.Equal(MediaDeliveryOutcome.Invalid, (await _sut.GetAsync(Id, 1, query)).Outcome);
    }

    [Fact]
    public async Task ASignatureForAnotherVersion_IsRefused()
    {
        var query = Signed(new MediaRendition(320, null, null, null));

        Assert.Equal(MediaDeliveryOutcome.Invalid, (await _sut.GetAsync(Id, 2, query)).Outcome);
    }

    [Fact]
    public async Task AWidthOutsideTheSet_IsRefused()
    {
        Assert.Equal(MediaDeliveryOutcome.Invalid, (await _sut.GetAsync(Id, 1, Query(("w", "500"), ("sig", "x")))).Outcome);
    }

    [Fact]
    public async Task ASignedWidth_RendersAWebP_AndStoresIt()
    {
        var rendition = new MediaRendition(640, null, null, null);
        _blobs.GetAsync(RenditionKey(rendition), Arg.Any<CancellationToken>()).Returns((MediaBlob?)null);

        var result = await _sut.GetAsync(Id, 1, Signed(rendition));

        Assert.Equal(MediaDeliveryOutcome.Found, result.Outcome);
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(RenderedBytes, await BytesAsync(result));
        Assert.Equal($"\"{Id:N}.1.{MediaRenditionQuery.Hash(rendition)}\"", result.ETag);
        _renderer.Received(1).Render(Arg.Is<byte[]>(b => b.SequenceEqual(OriginalBytes)),
            Arg.Is<MediaRenditionLayout>(p => p.Width == 640 && p.Height == 320));
        await _blobs.Received(1).PutAsync(RenditionKey(rendition), Arg.Any<Stream>(), "image/webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoredRendition_IsServedWithoutRenderingAgain()
    {
        var rendition = new MediaRendition(640, null, null, null);
        _blobs.GetAsync(RenditionKey(rendition), Arg.Any<CancellationToken>())
            .Returns(_ => new MediaBlob(new MemoryStream([5, 5, 5]), "image/webp", 3));

        var result = await _sut.GetAsync(Id, 1, Signed(rendition));

        Assert.Equal([5, 5, 5], await BytesAsync(result));
        Assert.Equal("image/webp", result.ContentType);
        _renderer.DidNotReceiveWithAnyArgs().Render(default!, default!);
    }

    // Two first requests for the same copy can both render it. Only one write lands, because
    // stored files are never overwritten; the other request still sends what it rendered.
    [Fact]
    public async Task ARenditionAnotherRequestStoredFirst_IsStillServed()
    {
        var rendition = new MediaRendition(640, null, null, null);
        _blobs.GetAsync(RenditionKey(rendition), Arg.Any<CancellationToken>()).Returns((MediaBlob?)null);
        _blobs.PutAsync(RenditionKey(rendition), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetAsync(Id, 1, Signed(rendition));

        Assert.Equal(MediaDeliveryOutcome.Found, result.Outcome);
        Assert.Equal(RenderedBytes, await BytesAsync(result));
    }

    [Theory]
    [InlineData("image/svg+xml", false)]
    [InlineData("image/gif", true)]
    [InlineData("image/webp", true)]
    public async Task ASignedRenditionOfAnSvgOrAnimation_ServesTheStoredFile(string contentType, bool animated)
    {
        UseVersion(Version(contentType, animated));

        var result = await _sut.GetAsync(Id, 1, Signed(new MediaRendition(640, null, null, null)));

        Assert.Equal(MediaDeliveryOutcome.Found, result.Outcome);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(OriginalBytes, await BytesAsync(result));
        Assert.Equal($"\"{Id:N}.1\"", result.ETag);
        _renderer.DidNotReceiveWithAnyArgs().Render(default!, default!);
    }

    [Fact]
    public async Task ARenditionForAnImageWithNoKnownSize_ServesTheStoredFile()
    {
        UseVersion(Version(width: null, height: null));

        var result = await _sut.GetAsync(Id, 1, Signed(new MediaRendition(640, null, null, null)));

        Assert.Equal(OriginalBytes, await BytesAsync(result));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!, default!);
    }
}
