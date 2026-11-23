using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Variants;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Variants;

// Groups the current uses of one image by how the page shows it: the whole image, a crop (with an
// aspect ratio and focal point), or a box that rich text sets. The key follows the renderers, so
// two uses that look the same on the page are one group.
public sealed class MediaVariantGrouperTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid PageOne = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PageTwo = new("22222222-2222-2222-2222-222222222222");

    private static MediaAssetVersionDto Png(int width = 1280, int height = 640) =>
        new(A, 1, MediaBlobKeys.Original(A, 1), "image/png", width, height, 100, "sha", false, DateTime.UtcNow, "u");

    private static MediaAssetVersionDto Svg() =>
        new(A, 1, MediaBlobKeys.Original(A, 1), "image/svg+xml", 100, 100, 100, "sha", false, DateTime.UtcNow, "u");

    private static JsonObject Image(Guid id, string crop = "", string aspect = "", string focal = "") =>
        new() { ["kind"] = "widget", ["type"] = "image", ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["crop"] = crop, ["aspect"] = aspect, ["focal"] = focal } };

    private static JsonObject RichText(string html) =>
        new() { ["kind"] = "widget", ["type"] = "richtext", ["props"] = new JsonObject { ["html"] = html } };

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    private static MediaVariantPageVersion Page(Guid id, string title, EmbeddedVersionState state, string content, params Guid[] recorded) =>
        new(id, title, state, content, recorded.Length == 0 ? [A] : recorded);

    [Fact]
    public void UsesWithNoSettings_AreOneOriginalGroup()
    {
        var variants = MediaVariantGrouper.Group(Png(),
            [Page(PageOne, "One", EmbeddedVersionState.Live, Tree(Image(A), Image(A)))], []);

        var only = Assert.Single(variants);
        Assert.True(only.Shape.IsOriginal);
        var use = Assert.Single(only.Uses);
        Assert.Equal((PageOne, "One", MediaUsageLocations.Widget, 2), (use.PageNodeId, use.Name, use.Location, use.Count));
    }

    [Fact]
    public void EachDistinctCrop_IsItsOwnGroup_AfterTheOriginal()
    {
        var content = Tree(Image(A), Image(A, aspect: "16:9", focal: "0.25,0.5"), Image(A, crop: "0,0,0.5,1"));

        var variants = MediaVariantGrouper.Group(Png(), [Page(PageOne, "One", EmbeddedVersionState.Live, content)], []);

        Assert.Equal(3, variants.Count);
        Assert.True(variants[0].Shape.IsOriginal);
        Assert.Equal(new MediaAspectRatio(16, 9), variants[1].Shape.Cut!.Aspect);
        Assert.Equal(new MediaFocalPoint(0.25m, 0.5m), variants[1].Shape.Cut!.Focal);
        Assert.Equal(new MediaCrop(0m, 0m, 0.5m, 1m), variants[2].Shape.Cut!.Crop);
    }

    [Fact]
    public void CropsThatRoundToTheSameValues_AreOneGroup()
    {
        var content = Tree(Image(A, crop: "0,0,0.5,1"), Image(A, crop: "0.000,0.0,0.500,1.0"), Image(A, crop: "0,0,0.5004,1"));

        var variants = MediaVariantGrouper.Group(Png(), [Page(PageOne, "One", EmbeddedVersionState.Live, content)], []);

        var only = Assert.Single(variants);
        Assert.Equal(3, only.Uses.Single().Count);
    }

    [Fact]
    public void AFocalPointWithNoAspectRatio_IsTheOriginal()
    {
        var variants = MediaVariantGrouper.Group(Png(),
            [Page(PageOne, "One", EmbeddedVersionState.Live, Tree(Image(A, focal: "0.1,0.1")))], []);

        Assert.True(Assert.Single(variants).Shape.IsOriginal);
    }

    [Fact]
    public void AnSvgIsNeverCropped_SoEveryWidgetUseIsTheOriginal()
    {
        var variants = MediaVariantGrouper.Group(Svg(),
            [Page(PageOne, "One", EmbeddedVersionState.Live, Tree(Image(A, crop: "0,0,0.5,1"), Image(A, aspect: "1:1")))], []);

        Assert.True(Assert.Single(variants).Shape.IsOriginal);
    }

    [Fact]
    public void RichTextBoxes_AreGroupedBySize_AndTheFilesOwnSizeIsTheOriginal()
    {
        var html = $"<img data-media-id=\"{A}\" width=\"1280\" height=\"640\">" +
                   $"<img data-media-id=\"{A}\">" +
                   $"<img data-media-id=\"{A}\" width=\"320\" height=\"320\">" +
                   $"<img data-media-id=\"{A}\" width=\"400\">";

        var variants = MediaVariantGrouper.Group(Png(), [Page(PageOne, "One", EmbeddedVersionState.Live, Tree(RichText(html)))], []);

        Assert.Equal(3, variants.Count);
        Assert.True(variants[0].Shape.IsOriginal);
        Assert.Equal(2, variants[0].Uses.Single().Count);
        Assert.Equal((320, 320), (variants[1].Shape.BoxWidth, variants[1].Shape.BoxHeight));
        // One side only: the other side follows the shape of the file, as the page does.
        Assert.Equal((400, 200), (variants[2].Shape.BoxWidth, variants[2].Shape.BoxHeight));
    }

    [Fact]
    public void LiveAndDraftVersionsWithTheSameUse_AreOneRowWithBothStates()
    {
        var content = Tree(Image(A, aspect: "1:1"));

        var variants = MediaVariantGrouper.Group(Png(),
        [
            Page(PageOne, "One", EmbeddedVersionState.Draft, content),
            Page(PageOne, "One", EmbeddedVersionState.Live, content),
        ], []);

        var use = Assert.Single(Assert.Single(variants).Uses);
        Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Draft], use.States);
        Assert.Equal(1, use.Count);
    }

    [Fact]
    public void ADraftThatChangesTheCrop_PutsThePageUnderBothGroups()
    {
        var variants = MediaVariantGrouper.Group(Png(),
        [
            Page(PageOne, "One", EmbeddedVersionState.Live, Tree(Image(A))),
            Page(PageOne, "One", EmbeddedVersionState.Draft, Tree(Image(A, aspect: "1:1"))),
        ], []);

        Assert.Equal(2, variants.Count);
        Assert.Equal([EmbeddedVersionState.Live], variants[0].Uses.Single().States);
        Assert.Equal([EmbeddedVersionState.Draft], variants[1].Uses.Single().States);
    }

    [Fact]
    public void OtherImages_AreLeftOut_AndPagesAreInTitleOrder_ThenBlocks()
    {
        var variants = MediaVariantGrouper.Group(Png(),
        [
            Page(PageTwo, "Zebra", EmbeddedVersionState.Live, Tree(Image(A), Image(B, aspect: "1:1"))),
            Page(PageOne, "Apple", EmbeddedVersionState.Live, Tree(Image(A))),
        ],
        [new MediaVariantBlock(7, "a-block", $"<p><img data-media-id=\"{A}\"></p>", [A])]);

        var uses = Assert.Single(variants).Uses;
        Assert.Equal(["Apple", "Zebra", "a-block"], uses.Select(u => u.Name));
        Assert.Equal(7, uses[2].ContentBlockId);
        Assert.Null(uses[2].PageNodeId);
        Assert.Empty(uses[2].States);
    }

    [Fact]
    public void ARecordedUseThatTheReaderCannotSee_CountsAsTheOriginal()
    {
        // A markdown image is recorded by its address, but it is not an img element.
        var variants = MediaVariantGrouper.Group(Png(),
            [Page(PageOne, "Wiki", EmbeddedVersionState.Live, $"![Logo](/media/{A}/1/logo.png)")], []);

        var only = Assert.Single(variants);
        Assert.True(only.Shape.IsOriginal);
        Assert.Equal("Wiki", only.Uses.Single().Name);
    }

    [Fact]
    public void AVersionThatDoesNotRecordTheImage_IsLeftOut()
    {
        var variants = MediaVariantGrouper.Group(Png(),
            [Page(PageOne, "Other", EmbeddedVersionState.Live, "<p>No images</p>", B)], []);

        Assert.Empty(variants);
    }
}
