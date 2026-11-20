using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// Which part of the stored image a resized copy shows, and how big the copy is. A copy is never
// larger than the part of the image it shows, so a small image is never stretched.
public sealed class MediaRenditionGeometryTests
{
    [Fact]
    public void AWidth_ScalesTheWholeImage()
    {
        var layout = MediaRenditionGeometry.Compute(2000, 1000, new MediaRendition(640, null, null, null));

        Assert.Equal(new MediaPixelRect(0, 0, 2000, 1000), layout.Source);
        Assert.Equal((640, 320), (layout.Width, layout.Height));
    }

    [Fact]
    public void AWidthLargerThanTheImage_KeepsTheImageSize()
    {
        var layout = MediaRenditionGeometry.Compute(500, 250, new MediaRendition(1920, null, null, null));

        Assert.Equal((500, 250), (layout.Width, layout.Height));
    }

    [Fact]
    public void ACrop_TakesThatPartOfTheImage()
    {
        var layout = MediaRenditionGeometry.Compute(1000, 800, new MediaRendition(null, new MediaCrop(0.1m, 0.25m, 0.5m, 0.5m), null, null));

        Assert.Equal(new MediaPixelRect(100, 200, 500, 400), layout.Source);
        Assert.Equal((500, 400), (layout.Width, layout.Height));
    }

    [Fact]
    public void ACropThenAWidth_IsNeverScaledUp()
    {
        var layout = MediaRenditionGeometry.Compute(1000, 800, new MediaRendition(960, new MediaCrop(0, 0, 0.5m, 0.5m), null, null));

        Assert.Equal((500, 400), (layout.Width, layout.Height));
    }

    [Fact]
    public void AnAspect_CentresTheLargestAreaThatFits_WhenThereIsNoFocalPoint()
    {
        var layout = MediaRenditionGeometry.Compute(1000, 1000, new MediaRendition(null, null, new MediaAspectRatio(2, 1), null));

        Assert.Equal(new MediaPixelRect(0, 250, 1000, 500), layout.Source);
        Assert.Equal((1000, 500), (layout.Width, layout.Height));
    }

    [Fact]
    public void AnAspect_MovesTheAreaTowardsTheFocalPoint_WithoutLeavingTheImage()
    {
        var top = MediaRenditionGeometry.Compute(1000, 1000, new MediaRendition(null, null, new MediaAspectRatio(2, 1), new MediaFocalPoint(0.5m, 0.1m)));
        var middle = MediaRenditionGeometry.Compute(1000, 1000, new MediaRendition(null, null, new MediaAspectRatio(2, 1), new MediaFocalPoint(0.5m, 0.4m)));
        var bottom = MediaRenditionGeometry.Compute(1000, 1000, new MediaRendition(null, null, new MediaAspectRatio(2, 1), new MediaFocalPoint(0.5m, 1m)));

        Assert.Equal(new MediaPixelRect(0, 0, 1000, 500), top.Source);
        Assert.Equal(new MediaPixelRect(0, 150, 1000, 500), middle.Source);
        Assert.Equal(new MediaPixelRect(0, 500, 1000, 500), bottom.Source);
    }

    [Fact]
    public void AnAspect_FitsInsideTheCrop_AndTheFocalPointIsOnTheWholeImage()
    {
        // The crop is the right half; the focal point is near the bottom-right of the whole image.
        var layout = MediaRenditionGeometry.Compute(1000, 1000, new MediaRendition(320, new MediaCrop(0.5m, 0, 0.5m, 1), new MediaAspectRatio(1, 1), new MediaFocalPoint(0.9m, 0.9m)));

        Assert.Equal(new MediaPixelRect(500, 500, 500, 500), layout.Source);
        Assert.Equal((320, 320), (layout.Width, layout.Height));
    }

    [Fact]
    public void ATinyCrop_IsAtLeastOnePixel()
    {
        var layout = MediaRenditionGeometry.Compute(10, 10, new MediaRendition(320, new MediaCrop(0.999m, 0.999m, 0.001m, 0.001m), null, null));

        Assert.Equal(1, layout.Source.Width);
        Assert.Equal(1, layout.Source.Height);
        Assert.True(layout.Source.X + layout.Source.Width <= 10);
        Assert.True(layout.Source.Y + layout.Source.Height <= 10);
        Assert.Equal((1, 1), (layout.Width, layout.Height));
    }

    [Fact]
    public void AVeryWideAspect_IsAtLeastOnePixelHigh()
    {
        var layout = MediaRenditionGeometry.Compute(100, 100, new MediaRendition(null, null, new MediaAspectRatio(1000, 1), null));

        Assert.Equal(100, layout.Source.Width);
        Assert.Equal(1, layout.Source.Height);
    }
}
