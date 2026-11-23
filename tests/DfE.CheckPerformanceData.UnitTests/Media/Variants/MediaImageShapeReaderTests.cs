using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Variants;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Variants;

// Reads how each use of a library image is shown: an Image widget's crop, aspect ratio and focal
// point, and the width and height that rich text gives an img. The settings are read the way the
// renderers read them, so a value the page would ignore is not read either.
public sealed class MediaImageShapeReaderTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static JsonObject Widget(string type, JsonObject props) =>
        new() { ["kind"] = "widget", ["type"] = type, ["props"] = props };

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    private static JsonObject Image(string crop = "", string aspect = "", string focal = "") =>
        Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["crop"] = crop, ["aspect"] = aspect, ["focal"] = focal });

    [Fact]
    public void AnImageWidget_GivesItsCrop_AspectRatio_AndFocalPoint()
    {
        var use = MediaImageUseReader.ReadShapes(Tree(Image("0,0,0.5,1", "16:9", "0.25,0.5"))).Single();

        Assert.Equal(new MediaImageShapeUse(A, MediaUsageLocations.Widget, "0.0",
            new MediaCrop(0m, 0m, 0.5m, 1m), new MediaAspectRatio(16, 9), new MediaFocalPoint(0.25m, 0.5m), null, null), use);
    }

    [Fact]
    public void AFocalPointWithNoAspectRatio_IsLeftOut_AsThePageIgnoresIt()
    {
        var use = MediaImageUseReader.ReadShapes(Tree(Image(focal: "0.25,0.5"))).Single();

        Assert.Null(use.Focal);
        Assert.Null(use.Aspect);
        Assert.Null(use.Crop);
    }

    [Fact]
    public void ACropOrAspectRatioThePageWouldRefuse_IsLeftOut()
    {
        var use = MediaImageUseReader.ReadShapes(Tree(Image("0.8,0,0.5,1", "7:5"))).Single();

        Assert.Null(use.Crop);
        Assert.Null(use.Aspect);
    }

    [Fact]
    public void ARichTextImage_GivesTheWidthAndHeightTheEditorSet()
    {
        var html = $"<p><img data-media-id=\"{A}\" width=\"400\" height=\"200\" alt=\"x\"><img data-media-id=\"{A}\" width=\"300\" alt=\"y\"><img data-media-id=\"{A}\" alt=\"z\"></p>";

        var uses = MediaImageUseReader.ReadShapes(Tree(Widget("richtext", new JsonObject { ["html"] = html })));

        Assert.Equal([(400, 200), (300, (int?)null), ((int?)null, (int?)null)],
            uses.Select(u => (u.BoxWidth, u.BoxHeight)).ToList());
        Assert.All(uses, u => Assert.Equal(MediaUsageLocations.RichText, u.Location));
        Assert.All(uses, u => Assert.Null(u.Crop));
    }

    [Fact]
    public void ARichTextImage_GivesItsBoxMarker()
    {
        var html = $"<img data-media-id=\"{A}\" width=\"400\" height=\"200\" data-media-box=\"follow\">" +
                   $"<img data-media-id=\"{A}\" width=\"400\" height=\"400\" data-media-box=\"fixed\">" +
                   $"<img data-media-id=\"{A}\" width=\"400\" height=\"400\">";

        var uses = MediaImageUseReader.ReadRichTextShapes(html);

        Assert.Equal(["follow", "fixed", null], uses.Select(u => u.BoxMarker).ToList());
    }

    [Fact]
    public void AContentBlockValue_IsReadAsRichText()
    {
        var use = MediaImageUseReader.ReadRichTextShapes($"<img src=\"/media/{A}/1/a.png\" width=\"320\" height=\"320\">").Single();

        Assert.Equal((A, 320, 320, (string?)null), (use.AssetId, use.BoxWidth, use.BoxHeight, use.WidgetPath));
    }

    [Fact]
    public void ReadingShapes_DoesNotChangeWhatTheAltTextReportReads()
    {
        var content = Tree(Widget("image", new JsonObject { ["mediaId"] = A.ToString(), ["alt"] = "photo", ["crop"] = "0,0,0.5,1" }));

        Assert.Equal([new MediaImageUse(A, MediaUsageLocations.Widget, "0.0", "photo", false, null)], MediaImageUseReader.Read(content));
    }
}
