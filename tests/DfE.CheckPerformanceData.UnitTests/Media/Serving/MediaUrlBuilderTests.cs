using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using Microsoft.AspNetCore.WebUtilities;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// Pages call the builder when they render, so the URLs it makes must be exactly the ones the
// media endpoint accepts: the settings it writes parse back to the same values, and the signature
// matches them.
public sealed class MediaUrlBuilderTests
{
    private static readonly Guid Id = Guid.Parse("8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly MediaUrlSigner _signer = new(new FixedSigningKey(Enumerable.Repeat((byte)9, 32).ToArray()));
    private readonly MediaUrlBuilder _sut;

    public MediaUrlBuilderTests()
    {
        _sut = new MediaUrlBuilder(_signer);
    }

    private static MediaAssetVersionDto Version(string contentType = "image/jpeg", int? width = 2000, int? height = 1000, bool animated = false, int version = 3) =>
        new(Id, version, MediaBlobKeys.Original(Id, version), contentType, width, height, 100, "sha", animated, DateTime.UtcNow, "u");

    private static MediaAssetDto Asset(MediaAssetVersionDto latest, string slug = "school-photo") =>
        new(Id, "School photo.jpg", slug, null, null, null, [], latest.Version, latest, DateTime.UtcNow, "u", DateTime.UtcNow, "u");

    private (string Path, Dictionary<string, string?> Query) Split(string url)
    {
        var at = url.IndexOf('?');
        var path = at < 0 ? url : url[..at];
        var query = at < 0
            ? new Dictionary<string, string?>()
            : QueryHelpers.ParseQuery(url[at..]).ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
        return (path, query);
    }

    private void AssertSigned(string url, int version)
    {
        var (_, query) = Split(url);
        var parsed = MediaRenditionQuery.Parse(query);
        Assert.True(parsed.IsValid);
        Assert.NotNull(parsed.Rendition);
        Assert.True(_signer.IsValid(Id, version, parsed.Rendition!, parsed.Signature));
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/gif", "gif")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/svg+xml", "svg")]
    public void Original_IsThePathWithTheSlugAndExtension(string contentType, string extension)
    {
        var version = Version(contentType);

        Assert.Equal($"/media/{Id:D}/3/school-photo.{extension}", _sut.Original(Asset(version), version));
    }

    [Fact]
    public void Original_OfAnOlderVersion_NamesThatVersion()
    {
        var old = Version(version: 1);

        Assert.StartsWith($"/media/{Id:D}/1/", _sut.Original(Asset(Version(version: 2)), old));
    }

    [Fact]
    public void Rendition_IsAWebPWithSignedSettings()
    {
        var version = Version();

        var url = _sut.Rendition(Asset(version), version, 640, new MediaCrop(0.1m, 0.2m, 0.5m, 0.5m), new MediaAspectRatio(16, 9), new MediaFocalPoint(0.3m, 0.4m));

        var (path, query) = Split(url);
        Assert.Equal($"/media/{Id:D}/3/school-photo.webp", path);
        Assert.Equal("640", query["w"]);
        Assert.Equal("0.1,0.2,0.5,0.5", query["crop"]);
        Assert.Equal("16:9", query["ar"]);
        Assert.Equal("0.3,0.4", query["focal"]);
        AssertSigned(url, 3);
    }

    [Fact]
    public void Rendition_RoundsTheCropToThreePlaces_BeforeSigning()
    {
        var version = Version();

        var url = _sut.Rendition(Asset(version), version, 320, new MediaCrop(0.12345m, 0, 0.5m, 0.5m));

        Assert.Equal("0.123,0,0.5,0.5", Split(url).Query["crop"]);
        AssertSigned(url, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(3840)]
    public void Rendition_RefusesAWidthOutsideTheFixedSet(int width)
    {
        var version = Version();

        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.Rendition(Asset(version), version, width));
    }

    [Fact]
    public void Rendition_RefusesAFocalPointWithoutAnAspect()
    {
        var version = Version();

        Assert.Throws<ArgumentException>(() => _sut.Rendition(Asset(version), version, 320, focal: new MediaFocalPoint(0.5m, 0.5m)));
    }

    [Theory]
    [InlineData("image/svg+xml", false, 100)]
    [InlineData("image/gif", true, 100)]
    [InlineData("image/webp", true, 100)]
    [InlineData("image/png", false, null)]
    public void Rendition_OfAnImageThatIsNeverResized_IsTheOriginal(string contentType, bool animated, int? width)
    {
        var version = Version(contentType, width, width, animated);
        var asset = Asset(version);

        Assert.Equal(_sut.Original(asset, version), _sut.Rendition(asset, version, 640, new MediaCrop(0, 0, 0.5m, 0.5m)));
        Assert.Equal("", _sut.SrcSet(asset, version));
    }

    [Fact]
    public void SrcSet_ListsTheFixedWidthsUpToTheSourceWidth()
    {
        var version = Version(width: 1000, height: 500);

        var entries = _sut.SrcSet(Asset(version), version).Split(", ");

        Assert.Equal(["320w", "640w", "960w", "1000w"], entries.Select(e => e.Split(' ')[1]));
        Assert.All(entries, e => AssertSigned(e.Split(' ')[0], 3));
        Assert.Contains("w=1280", entries[3]);
    }

    [Fact]
    public void SrcSet_OfAnImageExactlyAFixedWidth_StopsThere()
    {
        var version = Version(width: 960, height: 500);

        var entries = _sut.SrcSet(Asset(version), version).Split(", ");

        Assert.Equal(["320w", "640w", "960w"], entries.Select(e => e.Split(' ')[1]));
    }

    [Fact]
    public void SrcSet_UsesTheWidthAfterCropping()
    {
        var version = Version(width: 2000, height: 1000);

        var entries = _sut.SrcSet(Asset(version), version, new MediaCrop(0, 0, 0.25m, 0.5m)).Split(", ");

        Assert.Equal(["320w", "500w"], entries.Select(e => e.Split(' ')[1]));
    }

    [Fact]
    public void SrcSet_OfASmallImage_HasOneEntryAtItsOwnWidth()
    {
        var version = Version(width: 200, height: 100);

        var entries = _sut.SrcSet(Asset(version), version).Split(", ");

        Assert.Equal(["200w"], entries.Select(e => e.Split(' ')[1]));
    }
}
