using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Reports;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Reports;

// Reads each use of a library image from stored content with the alt text the page gives it, so
// the accessibility report can check the text that readers get. It follows the renderers: an
// Image widget's alt prop, and a rich-text img's alt attribute.
public sealed class MediaImageUseReaderTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static JsonObject Widget(string type, JsonObject props) =>
        new() { ["kind"] = "widget", ["type"] = type, ["props"] = props };

    private static JsonObject RichText(string html) => Widget("richtext", new JsonObject { ["html"] = html });

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    [Fact]
    public void AnImageWidget_GivesItsAltText_Caption_AndPath()
    {
        var content = Tree(
            Widget("heading", new JsonObject { ["text"] = "Hello" }),
            Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["alt"] = "photo", ["caption"] = "Our school", ["decorative"] = "false" }));

        var uses = MediaImageUseReader.Read(content);

        Assert.Equal([new MediaImageUse(A, MediaUsageLocations.Widget, "0.1", "photo", false, "Our school")], uses);
    }

    [Fact]
    public void AnImageWidget_WithNoAltProp_GivesNullAltText_AndADecorativeOneSaysSo()
    {
        var content = Tree(
            Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["alt"] = "  " }),
            Widget("image", new JsonObject { ["mediaId"] = B.ToString(), ["alt"] = "", ["decorative"] = "true" }));

        var uses = MediaImageUseReader.Read(content);

        Assert.Equal(2, uses.Count);
        Assert.Null(uses[0].Alt);
        Assert.False(uses[0].Decorative);
        Assert.True(uses[1].Decorative);
    }

    [Fact]
    public void ImagesInRegions_HaveTheirTreePath()
    {
        var region = new JsonObject
        {
            ["kind"] = "region",
            ["columns"] = new JsonArray(
                new JsonArray(),
                new JsonArray(Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["alt"] = "A chart of results" }))),
        };

        var uses = MediaImageUseReader.Read(Tree(region));

        Assert.Equal("0.0-1.0", uses.Single().WidgetPath);
    }

    [Fact]
    public void RichTextImages_GiveTheirAltAttribute_OrNullWhenThereIsNone()
    {
        var content = Tree(RichText(
            $"<p><img data-media-id=\"{A}\" src=\"/media/{A}/a.png\" alt=\"image\"></p>" +
            $"<img src=\"/media/{B}/1/b.png\">"));

        var uses = MediaImageUseReader.Read(content);

        Assert.Equal(
        [
            new MediaImageUse(A, MediaUsageLocations.RichText, "0.0", "image", false, null),
            new MediaImageUse(B, MediaUsageLocations.RichText, "0.0", null, false, null),
        ], uses);
    }

    [Fact]
    public void ARichTextImage_WithAnEmptyAlt_IsEmptyText_ButARolePresentationImageIsDecorative()
    {
        var content = Tree(RichText(
            $"<img data-media-id=\"{A}\" alt=\"\"><img data-media-id=\"{B}\" alt=\"\" role=\"presentation\">"));

        var uses = MediaImageUseReader.Read(content);

        Assert.Equal("", uses[0].Alt);
        Assert.False(uses[0].Decorative);
        Assert.True(uses[1].Decorative);
    }

    [Fact]
    public void ARichTextImageInAFigure_GivesTheFigcaptionAsItsCaption()
    {
        var html = $"<figure><img data-media-id=\"{A}\" alt=\"Pupils reading\"><figcaption> Pupils reading </figcaption></figure>";

        var use = MediaImageUseReader.ReadRichText(html).Single();

        Assert.Equal("Pupils reading", use.Caption);
        Assert.Null(use.WidgetPath);
    }

    [Fact]
    public void ABodyThatIsNotAWidgetTree_IsReadAsHtml()
    {
        var uses = MediaImageUseReader.Read($"<p>Wiki</p><img data-media-id=\"{A}\" alt=\"Logo\">");

        Assert.Equal([new MediaImageUse(A, MediaUsageLocations.RichText, null, "Logo", false, null)], uses);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[{\"kind\":\"widget\"")]
    [InlineData("{\"not\":\"a tree\"}")]
    public void ContentItCannotRead_GivesNoUses(string? content)
    {
        Assert.Empty(MediaImageUseReader.Read(content));
    }

    [Fact]
    public void PlainTextProps_AreNotReadAsHtml()
    {
        var content = Tree(Widget("heading", new JsonObject { ["text"] = $"<img data-media-id=\"{A}\" alt=\"x\">" }));

        Assert.Empty(MediaImageUseReader.Read(content));
    }

    // The editor's decorative option writes both: an empty alt and role="presentation". A role with
    // no alt attribute at all is not decorative, because the page then shows the library's text.
    [Fact]
    public void ARolePresentationImage_WithNoAltAttribute_IsNotDecorative()
    {
        var content = Tree(RichText($"<img data-media-id=\"{A}\" role=\"presentation\">"));

        var use = MediaImageUseReader.Read(content).Single();

        Assert.Null(use.Alt);
        Assert.False(use.Decorative);
    }
}
