using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Application.Media.Variants;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Variants;

// The service reads the current content that records a use of the images, in two queries for any
// number of images, and groups each image's uses.
public sealed class MediaVariantServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private readonly IMediaReportStore _store = Substitute.For<IMediaReportStore>();
    private readonly MediaVariantService _sut;

    public MediaVariantServiceTests() =>
        _sut = new MediaVariantService(_store, new FakeTimeProvider(new DateTimeOffset(Now)));

    private static MediaAssetDto Asset()
    {
        var id = Guid.NewGuid();
        var version = new MediaAssetVersionDto(id, 1, MediaBlobKeys.Original(id, 1), "image/png", 1280, 640, 10, "sha", false, Now, "u");
        return new MediaAssetDto(id, "a.png", "a", null, null, null, [], 1, version, Now, "u", Now, "u");
    }

    private static string Image(Guid id, string aspect) =>
        new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "image",
            ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["aspect"] = aspect },
        }).ToJsonString();

    [Fact]
    public async Task Variants_ReadTheCurrentContentOfTheOneImage()
    {
        var asset = Asset();
        _store.ListCurrentPageVersionsUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Now, Arg.Any<CancellationToken>())
            .Returns([new MediaVariantPageVersion(Guid.NewGuid(), "P", EmbeddedVersionState.Live, Image(asset.Id, "1:1"), [asset.Id])]);
        _store.ListContentBlocksUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var variants = await _sut.GetVariantsAsync(asset);

        Assert.False(Assert.Single(variants).Shape.IsOriginal);
        await _store.Received(1).ListCurrentPageVersionsUsingAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { asset.Id })), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Counts_UseOneReadForAllTheImages_AndGiveZeroForAnUnusedImage()
    {
        var (a, b, c) = (Asset(), Asset(), Asset());
        _store.ListCurrentPageVersionsUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Now, Arg.Any<CancellationToken>())
            .Returns(
            [
                new MediaVariantPageVersion(Guid.NewGuid(), "P", EmbeddedVersionState.Live, Image(a.Id, "1:1"), [a.Id]),
                new MediaVariantPageVersion(Guid.NewGuid(), "Q", EmbeddedVersionState.Live, Image(a.Id, ""), [a.Id]),
                new MediaVariantPageVersion(Guid.NewGuid(), "R", EmbeddedVersionState.Live, Image(b.Id, "16:9"), [b.Id]),
            ]);
        _store.ListContentBlocksUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var counts = await _sut.CountVariantsAsync([a, b, c]);

        Assert.Equal((2, 1, 0), (counts[a.Id], counts[b.Id], counts[c.Id]));
        await _store.Received(1).ListCurrentPageVersionsUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Now, Arg.Any<CancellationToken>());
        await _store.Received(1).ListContentBlocksUsingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Counts_ForNoImages_ReadNothing()
    {
        Assert.Empty(await _sut.CountVariantsAsync([]));
        await _store.DidNotReceiveWithAnyArgs().ListCurrentPageVersionsUsingAsync(default!, default, default);
    }
}
