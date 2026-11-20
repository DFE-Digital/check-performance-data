using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media;

// Finds the library images a page version's content uses: Image widgets by their mediaId prop, and
// <img> elements inside rich text by data-media-id or by a /media/{id}/ src. A save must never
// fail because of the content, so anything it cannot read gives no references.
public sealed class MediaReferenceExtractorTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static JsonObject Widget(string type, JsonObject props) =>
        new() { ["kind"] = "widget", ["type"] = type, ["props"] = props };

    private static JsonObject RichText(string html) => Widget("richtext", new JsonObject { ["html"] = html });

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    [Fact]
    public void ImageWidget_IsFoundByItsMediaIdProp_AtItsTreePath()
    {
        var content = Tree(
            Widget("heading", new JsonObject { ["level"] = 2, ["text"] = "Hello" }),
            Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["alt"] = "A school" }));

        var refs = MediaReferenceExtractor.Extract(content);

        Assert.Equal([new MediaReference(A, MediaUsageLocations.Widget, "0.1")], refs);
    }

    [Fact]
    public void AWidgetWithABlankOrInvalidMediaId_GivesNothing()
    {
        var content = Tree(
            Widget("image", new JsonObject { ["mediaId"] = "" }),
            Widget("image", new JsonObject { ["mediaId"] = "not-a-guid" }),
            Widget("image", new JsonObject { ["mediaId"] = 42 }),
            Widget("image", new JsonObject()));

        Assert.Empty(MediaReferenceExtractor.Extract(content));
    }

    [Fact]
    public void RichText_FindsAnImage_ByItsDataAttribute()
    {
        var content = Tree(RichText($"""<p>Intro</p><img data-media-id="{A}" alt="x">"""));

        Assert.Equal([new MediaReference(A, MediaUsageLocations.RichText, "0.0")], MediaReferenceExtractor.Extract(content));
    }

    [Theory]
    [InlineData("/media/{0}/2/photo.png")]
    [InlineData("/media/{0}/1/photo.png?w=640&sig=abc")]
    [InlineData("https://example.education.gov.uk/media/{0}/3/photo.webp")]
    [InlineData("/MEDIA/{0}/1/photo.png")]
    public void RichText_FindsAnImage_ByItsMediaSrc(string src)
    {
        var content = Tree(RichText($"""<img src="{string.Format(src, A)}" alt="x">"""));

        Assert.Equal([new MediaReference(A, MediaUsageLocations.RichText, "0.0")], MediaReferenceExtractor.Extract(content));
    }

    [Theory]
    [InlineData("/images/{0}/photo.png")]
    [InlineData("/media/not-a-guid/1/photo.png")]
    [InlineData("/somewhere/media/{0}/1/photo.png")]
    [InlineData("data:image/png;base64,AAAA")]
    public void RichText_IgnoresOtherSources(string src)
    {
        var content = Tree(RichText($"""<img src="{string.Format(src, A)}" alt="x">"""));

        Assert.Empty(MediaReferenceExtractor.Extract(content));
    }

    // The data attribute names the image the editor chose; a src that disagrees is not trusted.
    [Fact]
    public void RichText_PrefersTheDataAttribute_OverTheSrc()
    {
        var content = Tree(RichText($"""<img data-media-id="{A}" src="/media/{B}/1/other.png" alt="x">"""));

        Assert.Equal([new MediaReference(A, MediaUsageLocations.RichText, "0.0")], MediaReferenceExtractor.Extract(content));
    }

    [Fact]
    public void Duplicates_GiveOneReference_PerImageLocationAndWidget()
    {
        var content = Tree(
            RichText($"""<img data-media-id="{A}"><p><img src="/media/{A}/1/a.png"></p><img data-media-id="{B}">"""),
            Widget("image", new JsonObject { ["mediaId"] = A.ToString() }),
            RichText($"""<img data-media-id="{A}">"""));

        var refs = MediaReferenceExtractor.Extract(content);

        Assert.Equal(
            [
                new MediaReference(A, MediaUsageLocations.RichText, "0.0"),
                new MediaReference(B, MediaUsageLocations.RichText, "0.0"),
                new MediaReference(A, MediaUsageLocations.Widget, "0.1"),
                new MediaReference(A, MediaUsageLocations.RichText, "0.2"),
            ],
            refs);
    }

    [Fact]
    public void WidgetsNestedInRegions_AreFound_WithTheirFullTreePath()
    {
        var region = new JsonObject
        {
            ["kind"] = "region",
            ["layout"] = "halves",
            ["columns"] = new JsonArray(
                new JsonArray(RichText("<p>No image</p>")),
                new JsonArray(
                    RichText("<p>Nothing</p>"),
                    new JsonObject
                    {
                        ["kind"] = "region",
                        ["layout"] = "single",
                        ["columns"] = new JsonArray(new JsonArray(Widget("image", new JsonObject { ["mediaId"] = B.ToString() })))
                    }))
        };
        var content = Tree(RichText("<p>Top</p>"), region);

        Assert.Equal([new MediaReference(B, MediaUsageLocations.Widget, "0.1-1.1-0.0")], MediaReferenceExtractor.Extract(content));
    }

    // Only props that hold HTML are read as HTML. A card body is plain text, shown escaped.
    [Fact]
    public void ImageMarkupInAPlainTextProp_IsNotAUse()
    {
        var content = Tree(Widget("card", new JsonObject { ["title"] = "T", ["body"] = $"""<img data-media-id="{A}">""" }));

        Assert.Empty(MediaReferenceExtractor.Extract(content));
    }

    [Theory]
    [InlineData("<img data-media-id=\"{0}\"<<<>>><img src=")]
    [InlineData("<<<<p><img data-media-id='{0}' </div></div>")]
    public void GarbageHtml_StillFindsWhatItCan_AndDoesNotThrow(string html)
    {
        var content = Tree(RichText(string.Format(html, A)));

        var refs = MediaReferenceExtractor.Extract(content);

        Assert.All(refs, r => Assert.Equal(A, r.AssetId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{\"kind\":\"spaceship\"}]")]
    [InlineData("[{\"type\":\"image\"}]")]
    [InlineData("not json at all")]
    [InlineData("[1, \"two\", null]")]
    [InlineData("[{\"kind\":\"widget\",\"type\":\"richtext\",\"props\":{\"html\":42}}]")]
    public void ContentItCannotRead_GivesNothing(string? content)
    {
        Assert.Empty(MediaReferenceExtractor.Extract(content));
    }

    // A wiki page body is markdown or HTML, not a widget tree. An image in it still counts, so the
    // library does not delete a file that a wiki page shows.
    [Fact]
    public void ABodyThatIsNotAWidgetTree_IsReadAsRichText_WithNoWidgetPath()
    {
        var content = $"""
            # Heading

            ![A chart](/media/{A}/1/chart.png)

            <img data-media-id="{B}" alt="x">
            """;

        var refs = MediaReferenceExtractor.Extract(content);

        Assert.Equal(
            [
                new MediaReference(A, MediaUsageLocations.RichText, null),
                new MediaReference(B, MediaUsageLocations.RichText, null),
            ],
            refs.OrderBy(r => r.AssetId == A ? 0 : 1));
    }
}
