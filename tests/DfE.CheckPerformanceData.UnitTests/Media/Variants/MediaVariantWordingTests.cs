using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Variants;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Variants;

// The words that describe how a page shows an image: a title for each group, and its settings one
// to a line.
public sealed class MediaVariantWordingTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly MediaAssetVersionDto Png =
        new(A, 1, MediaBlobKeys.Original(A, 1), "image/png", 1280, 640, 100, "sha", false, DateTime.UtcNow, "u");

    private static MediaVariantShape Cut(MediaCrop? crop = null, MediaAspectRatio? aspect = null, MediaFocalPoint? focal = null) =>
        new(new MediaRendition(null, crop, aspect, focal), null, null);

    [Fact]
    public void TheOriginal_SaysItIsNotCropped()
    {
        var words = MediaVariantWording.Describe(MediaVariantShape.Original, Png);

        Assert.Equal("Original, not cropped", words.Title);
        Assert.Contains("The whole image, 1280 × 640 pixels.", words.Details);
    }

    [Theory]
    [InlineData("0.25", "0.5", "Cropped to 16:9, focal point centre-left")]
    [InlineData("0.5", "0.5", "Cropped to 16:9, focal point centre")]
    [InlineData("0.9", "0.1", "Cropped to 16:9, focal point top-right")]
    [InlineData("0.5", "0.8", "Cropped to 16:9, focal point bottom-centre")]
    public void AnAspectRatio_NamesTheFocalPoint(string x, string y, string title)
    {
        var focal = new MediaFocalPoint(decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(y, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(title, MediaVariantWording.Describe(Cut(aspect: new MediaAspectRatio(16, 9), focal: focal), Png).Title);
    }

    [Fact]
    public void AnAspectRatioWithNoFocalPoint_IsCutFromTheCentre()
    {
        var words = MediaVariantWording.Describe(Cut(aspect: new MediaAspectRatio(1, 1)), Png);

        Assert.Equal("Cropped to 1:1, from the centre", words.Title);
        Assert.Contains("Shows 640 × 640 pixels of the original.", words.Details);
    }

    [Fact]
    public void AFreeCrop_GivesTheAreaInPercentages_AndTheSizeItShows()
    {
        var words = MediaVariantWording.Describe(Cut(crop: new MediaCrop(0m, 0m, 0.5m, 1m)), Png);

        Assert.Equal("Cropped to part of the image", words.Title);
        Assert.Contains("Area used: 0% to 50% across, 0% to 100% down.", words.Details);
        Assert.Contains("Shows 640 × 640 pixels of the original.", words.Details);
    }

    [Fact]
    public void ACropAndAnAspectRatio_SayBoth_WithTheFocalPointInPercentages()
    {
        var words = MediaVariantWording.Describe(
            Cut(new MediaCrop(0.1m, 0m, 0.8m, 1m), new MediaAspectRatio(4, 3), new MediaFocalPoint(0.333m, 0.5m)), Png);

        Assert.Equal("Cropped to part of the image, then to 4:3, focal point centre", words.Title);
        Assert.Contains("Area used: 10% to 90% across, 0% to 100% down.", words.Details);
        Assert.Contains("Focal point: 33.3% across, 50% down.", words.Details);
    }

    [Fact]
    public void ARichTextBox_GivesItsSize_AndSaysWhenItStretchesTheImage()
    {
        var stretched = MediaVariantWording.Describe(new MediaVariantShape(null, 320, 320), Png);
        var scaled = MediaVariantWording.Describe(new MediaVariantShape(null, 400, 200), Png);

        Assert.Equal("Shown at 320 × 320 in rich text", stretched.Title);
        Assert.Contains("The box is not the shape of the image, so the image is stretched to fill it.", stretched.Details);
        Assert.Equal("Shown at 400 × 200 in rich text", scaled.Title);
        Assert.Contains("The whole image, scaled to this size.", scaled.Details);
    }

    [Fact]
    public void ARichTextBoxWithOneSide_SaysWhichSide()
    {
        var svg = Png with { ContentType = "image/svg+xml", Width = null, Height = null };

        Assert.Equal("Shown 300 pixels wide in rich text", MediaVariantWording.Describe(new MediaVariantShape(null, 300, null), svg).Title);
        Assert.Equal("Shown 300 pixels tall in rich text", MediaVariantWording.Describe(new MediaVariantShape(null, null, 300), svg).Title);
    }
}
