using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Embedded;

// Finds the images embedded as data: URIs in a page version's content, and points them at media
// library images. Only the src of each embedded <img> changes, and a data-media-id is added: every
// other attribute and every character outside those tags stays exactly as stored, so a body that
// mixes markdown and HTML is not reformatted.
public sealed class EmbeddedImageContentTests
{
    private const string Png = "data:image/png;base64," + DataImageUriTests.OnePixelPng;
    private const string Gif = "data:image/gif;base64,R0lGODlhAQABAAAAACw=";
    private static readonly Guid PngId = Guid.Parse("aaaaaaaa-38a9-4a65-9e3a-5f8f2f0f7a11");
    private static readonly Guid GifId = Guid.Parse("bbbbbbbb-38a9-4a65-9e3a-5f8f2f0f7a11");

    private static readonly IReadOnlyDictionary<string, LibraryImageSrc> Library = new Dictionary<string, LibraryImageSrc>
    {
        [Png] = new(PngId, $"/media/{PngId}/1/chart.png"),
        [Gif] = new(GifId, $"/media/{GifId}/3/spinner.gif"),
    };

    [Fact]
    public void Find_ListsEveryEmbeddedImage_InOrder_WithItsAltAndTitle()
    {
        var html = $"<p><img src=\"{Png}\" alt=\"Chart\"></p><p><img title=\"Spinner\" src='{Gif}'></p>" +
                   $"<img src=\"/media/{PngId}/1/a.png\" alt=\"library\"><img src=\"https://example.com/a.png\">";

        var found = EmbeddedImageContent.Find(html);

        Assert.Collection(found,
            i => { Assert.Equal(Png, i.Src); Assert.Equal("Chart", i.Alt); Assert.Null(i.Title); },
            i => { Assert.Equal(Gif, i.Src); Assert.Null(i.Alt); Assert.Equal("Spinner", i.Title); });
    }

    [Fact]
    public void Replace_PointsSeveralImagesAtTheLibrary()
    {
        var html = $"<p><img src=\"{Png}\" alt=\"Chart\"> and <img src=\"{Gif}\" alt=\"Spinner\"></p>";

        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.Equal(
            $"<p><img src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\" alt=\"Chart\"> and " +
            $"<img src=\"/media/{GifId}/3/spinner.gif\" data-media-id=\"{GifId}\" alt=\"Spinner\"></p>",
            result);
        Assert.Empty(EmbeddedImageContent.Find(result));
    }

    [Fact]
    public void TheSameImageTwice_IsFoundTwice_AndBothPointAtOneLibraryImage()
    {
        var html = $"<img src=\"{Png}\" alt=\"One\"><img src=\"{Png}\" alt=\"Two\">";

        Assert.Equal(2, EmbeddedImageContent.Find(html).Count);
        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.Equal(2, result.Split($"data-media-id=\"{PngId}\"").Length - 1);
        Assert.DoesNotContain("data:", result);
    }

    [Fact]
    public void Replace_KeepsAltWidthHeightStyleAndClass()
    {
        var html = $"<img class=\"right\" style=\"float: right; margin: 4px\" src=\"{Png}\" width=\"320\" height=\"160\" alt=\"Chart &amp; key\" />";

        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.Equal(
            $"<img class=\"right\" style=\"float: right; margin: 4px\" src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\" width=\"320\" height=\"160\" alt=\"Chart &amp; key\" />",
            result);
    }

    [Fact]
    public void Replace_LeavesEveryCharacterOutsideTheImageTags_AsStored()
    {
        var before = "# Heading\n\nSome *markdown* &amp; <b>html</b>  with  spaces\n\n";
        var after = "\n\n| a | b |\n|---|---|\n<p>Tail &nbsp;</p>";
        var html = before + $"<IMG SRC=\"{Png}\" ALT=\"x\">" + after;

        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.StartsWith(before, result);
        Assert.EndsWith(after, result);
    }

    [Fact]
    public void Replace_LeavesAnImageWithNoLibraryMatch_AsStored()
    {
        const string broken = "data:image/png;base64,not*base64!";
        const string notAnImage = "data:text/html;base64,PHNjcmlwdD4=";
        var html = $"<img src=\"{broken}\" alt=\"a\"><img src=\"{notAnImage}\"><img src=\"{Png}\" alt=\"b\">";

        Assert.Equal(3, EmbeddedImageContent.Find(html).Count);
        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.StartsWith($"<img src=\"{broken}\" alt=\"a\"><img src=\"{notAnImage}\">", result);
        Assert.EndsWith($"<img src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\" alt=\"b\">", result);
    }

    [Fact]
    public void AnSvgDataUri_WithMarkupInIt_IsOneImage()
    {
        const string svg = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1'/></svg>";
        var svgId = Guid.NewGuid();
        var html = $"<p><img src=\"{svg}\" alt=\"Box\"> after</p>";

        var found = Assert.Single(EmbeddedImageContent.Find(html));
        Assert.Equal(svg, found.Src);

        var result = EmbeddedImageContent.Replace(html,
            new Dictionary<string, LibraryImageSrc> { [svg] = new(svgId, $"/media/{svgId}/1/box.svg") });
        Assert.Equal($"<p><img src=\"/media/{svgId}/1/box.svg\" data-media-id=\"{svgId}\" alt=\"Box\"> after</p>", result);
    }

    [Fact]
    public void AnUnquotedSrc_IsReplacedWithAQuotedOne()
    {
        var html = $"<img src={Png} alt=x>";

        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.Equal($"<img src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\" alt=x>", result);
    }

    [Fact]
    public void AnExistingDataMediaId_IsReplaced_NotDuplicated()
    {
        var html = $"<img data-media-id=\"stale\" src=\"{Png}\">";

        var result = EmbeddedImageContent.Replace(html, Library);

        Assert.Equal($"<img src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\">", result);
    }

    // ── A content page's widget tree ─────────────────────────────────────────

    private static JsonObject RichText(string html) =>
        new() { ["kind"] = "widget", ["type"] = "richtext", ["props"] = new JsonObject { ["html"] = html } };

    private static string Tree() => new JsonArray(
        RichText($"<p><img src=\"{Png}\" alt=\"Top\"></p>"),
        new JsonObject
        {
            ["kind"] = "region",
            ["columns"] = new JsonArray(
                new JsonArray(RichText($"<img src=\"{Gif}\" alt=\"In a column\">")),
                new JsonArray(new JsonObject
                {
                    ["kind"] = "widget",
                    ["type"] = "heading",
                    // Not an HTML prop: the view shows it escaped, so it is not an image.
                    ["props"] = new JsonObject { ["text"] = $"<img src=\"{Png}\">" }
                }))
        }).ToJsonString();

    [Fact]
    public void InAWidgetTree_OnlyRichTextHtmlIsRead()
    {
        var found = EmbeddedImageContent.Find(Tree());

        Assert.Collection(found,
            i => Assert.Equal("Top", i.Alt),
            i => Assert.Equal("In a column", i.Alt));
    }

    [Fact]
    public void InAWidgetTree_ReplaceRewritesTheRichText_AndKeepsTheRest()
    {
        var result = EmbeddedImageContent.Replace(Tree(), Library);

        var tree = JsonNode.Parse(result)!.AsArray();
        Assert.Equal($"<p><img src=\"/media/{PngId}/1/chart.png\" data-media-id=\"{PngId}\" alt=\"Top\"></p>",
            tree[0]!["props"]!["html"]!.GetValue<string>());
        var columns = tree[1]!["columns"]!.AsArray();
        Assert.Equal($"<img src=\"/media/{GifId}/3/spinner.gif\" data-media-id=\"{GifId}\" alt=\"In a column\">",
            columns[0]![0]!["props"]!["html"]!.GetValue<string>());
        Assert.Equal($"<img src=\"{Png}\">", columns[1]![0]!["props"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Replace_WithNothingToChange_ReturnsTheContentUnchanged()
    {
        var tree = Tree();
        var none = new Dictionary<string, LibraryImageSrc>();

        Assert.Same(tree, EmbeddedImageContent.Replace(tree, none));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[not json")]
    [InlineData("{\"kind\":\"widget\"}")]
    public void Find_NeverThrows(string? content)
    {
        Assert.Empty(EmbeddedImageContent.Find(content));
    }
}
