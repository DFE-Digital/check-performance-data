using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Widget;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Widget;

// Live, draft and preview pages show each image's current version. A historic page version shows
// the version recorded for it, or the current one when none is recorded. A page loads all its
// images in one call, and later calls for the same images use what was loaded.
public sealed class MediaVersionResolverTests
{
    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();
    private readonly MediaVersionResolver _sut;

    public MediaVersionResolverTests()
    {
        _sut = new MediaVersionResolver(_assets);
        _assets.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => _library.Where(a => ci.Arg<IReadOnlyCollection<Guid>>().Contains(a.Id)).ToList());
        _assets.GetPinnedVersionsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => _pins.TryGetValue(ci.Arg<Guid>(), out var pins)
                ? pins.Where(p => ci.Arg<IReadOnlyCollection<Guid>>().Contains(p.AssetId)).ToList()
                : []);
    }

    private readonly List<MediaAssetDto> _library = [];
    private readonly Dictionary<Guid, List<MediaAssetVersionDto>> _pins = [];

    private static MediaAssetVersionDto Version(Guid id, int version) =>
        new(id, version, $"k{version}", "image/png", 640, 480, 10, "sha", false, DateTime.UtcNow, "u");

    private MediaAssetDto Image(int currentVersion = 1)
    {
        var id = Guid.NewGuid();
        var asset = new MediaAssetDto(id, "a.png", "a", null, null, null, [], currentVersion, Version(id, currentVersion),
            DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        _library.Add(asset);
        return asset;
    }

    private void Pin(Guid pageVersionId, Guid assetId, int version)
    {
        if (!_pins.TryGetValue(pageVersionId, out var list)) _pins[pageVersionId] = list = [];
        list.Add(Version(assetId, version));
    }

    [Fact]
    public async Task WithNoPageVersion_EachImageResolvesToItsCurrentVersion()
    {
        var a = Image(currentVersion: 3);
        var b = Image();

        var resolved = await _sut.ResolveAsync([a.Id, b.Id], null);

        Assert.Equal(3, resolved[a.Id].Version.Version);
        Assert.Equal(1, resolved[b.Id].Version.Version);
        Assert.Same(a, resolved[a.Id].Asset);
        await _assets.DidNotReceive().GetPinnedVersionsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnImageNotInTheLibrary_IsLeftOut()
    {
        var a = Image();
        var missing = Guid.NewGuid();

        var resolved = await _sut.ResolveAsync([a.Id, missing], null);

        Assert.True(resolved.ContainsKey(a.Id));
        Assert.False(resolved.ContainsKey(missing));
    }

    [Fact]
    public async Task AHistoricPageVersion_UsesTheVersionRecordedForIt()
    {
        var a = Image(currentVersion: 2);
        var pageVersion = Guid.NewGuid();
        Pin(pageVersion, a.Id, 1);

        var resolved = await _sut.ResolveAsync([a.Id], pageVersion);

        Assert.Equal(1, resolved[a.Id].Version.Version);
        Assert.Equal("k1", resolved[a.Id].Version.BlobKey);
        Assert.Same(a, resolved[a.Id].Asset);
    }

    [Fact]
    public async Task AHistoricPageVersion_WithNothingRecorded_UsesTheCurrentVersion()
    {
        var a = Image(currentVersion: 2);

        var resolved = await _sut.ResolveAsync([a.Id], Guid.NewGuid());

        Assert.Equal(2, resolved[a.Id].Version.Version);
    }

    [Fact]
    public async Task TwoRecordedUsesOfOneImage_DoNotThrow_AndTheNewerVersionWins()
    {
        // One row is kept for each place on the page, so an image used twice has two rows.
        var a = Image(currentVersion: 3);
        var pageVersion = Guid.NewGuid();
        Pin(pageVersion, a.Id, 1);
        Pin(pageVersion, a.Id, 2);

        var resolved = await _sut.ResolveAsync([a.Id], pageVersion);

        Assert.Equal(2, resolved[a.Id].Version.Version);
    }

    [Fact]
    public async Task ManyImages_AreLoadedInOneCall_AndLaterCallsUseWhatWasLoaded()
    {
        var images = Enumerable.Range(0, 5).Select(_ => Image()).ToList();
        var pageVersion = Guid.NewGuid();

        await _sut.ResolveAsync(images.Select(i => i.Id), pageVersion);
        foreach (var image in images)
        {
            var one = await _sut.ResolveAsync([image.Id], pageVersion);
            Assert.True(one.ContainsKey(image.Id));
        }

        await _assets.Received(1).GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _assets.Received(1).GetPinnedVersionsAsync(pageVersion, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnImageThatWasNotFound_IsNotLookedUpAgain()
    {
        var missing = Guid.NewGuid();

        await _sut.ResolveAsync([missing], null);
        await _sut.ResolveAsync([missing], null);

        await _assets.Received(1).GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALaterCall_LoadsOnlyTheImagesNotYetLoaded()
    {
        var a = Image();
        var b = Image();
        await _sut.ResolveAsync([a.Id], null);

        var resolved = await _sut.ResolveAsync([a.Id, b.Id], null);

        Assert.Equal(2, resolved.Count);
        await _assets.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(b.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CurrentAndHistoricResults_AreKeptApart()
    {
        var a = Image(currentVersion: 2);
        var pageVersion = Guid.NewGuid();
        Pin(pageVersion, a.Id, 1);

        var current = await _sut.ResolveAsync([a.Id], null);
        var historic = await _sut.ResolveAsync([a.Id], pageVersion);

        Assert.Equal(2, current[a.Id].Version.Version);
        Assert.Equal(1, historic[a.Id].Version.Version);
    }

    [Fact]
    public async Task PrimeAsync_LoadsEveryImageOnThePage_InOneCall_IncludingThoseInRegions()
    {
        var a = Image();
        var b = Image();
        var tree = new List<DfE.CheckPerformanceData.Application.ContentPages.ContentNode>
        {
            ImageWidgetNode(a.Id),
            new DfE.CheckPerformanceData.Application.ContentPages.RegionNode
            {
                Layout = DfE.CheckPerformanceData.Application.ContentPages.RegionLayout.Halves,
                Columns = [[ImageWidgetNode(b.Id)], []]
            },
        };

        await _sut.PrimeAsync(tree, null);
        await _sut.ResolveAsync([a.Id], null);
        await _sut.ResolveAsync([b.Id], null);

        await _assets.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(a.Id) && ids.Contains(b.Id)),
            Arg.Any<CancellationToken>());
    }

    private static DfE.CheckPerformanceData.Application.ContentPages.WidgetNode ImageWidgetNode(Guid id) =>
        new() { Type = "image", Props = new System.Text.Json.Nodes.JsonObject { ["mediaId"] = id.ToString() } };

    [Fact]
    public async Task NoIds_MakeNoCalls()
    {
        var resolved = await _sut.ResolveAsync([], Guid.NewGuid());

        Assert.Empty(resolved);
        await _assets.DidNotReceive().GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
