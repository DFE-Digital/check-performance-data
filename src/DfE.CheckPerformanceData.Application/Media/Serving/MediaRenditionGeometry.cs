namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Works out which pixels of a stored image a rendition shows, and its size. The steps:
/// 1. The crop, if any, picks an area of the image. Without one, the area is the whole image.
/// 2. An aspect ratio, if any, cuts the largest area of that shape from step 1. The focal point
///    (a point on the whole image) is put as near its centre as the area allows; without one,
///    the cut is centred.
/// 3. The width scales the result, but never above its own width, so nothing is stretched.
/// The URL builder uses this too, so a srcset names the width each copy really has.
/// </summary>
public static class MediaRenditionGeometry
{
    public static MediaRenditionLayout Compute(int sourceWidth, int sourceHeight, MediaRendition rendition)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);

        var area = Crop(sourceWidth, sourceHeight, rendition.Crop);
        if (rendition.Aspect is { } aspect) area = Cut(area, aspect, rendition.Focal, sourceWidth, sourceHeight);

        var width = Math.Min(rendition.Width ?? area.Width, area.Width);
        var height = Math.Max(1, (int)Math.Round((double)area.Height * width / area.Width, MidpointRounding.AwayFromZero));
        return new MediaRenditionLayout(area, width, height);
    }

    private static MediaPixelRect Crop(int sourceWidth, int sourceHeight, MediaCrop? crop)
    {
        if (crop is null) return new MediaPixelRect(0, 0, sourceWidth, sourceHeight);
        var (x, width) = Span(crop.X, crop.Width, sourceWidth);
        var (y, height) = Span(crop.Y, crop.Height, sourceHeight);
        return new MediaPixelRect(x, y, width, height);
    }

    // One side of the crop in pixels: at least one pixel, and inside the image.
    private static (int Start, int Length) Span(decimal start, decimal length, int size)
    {
        var pixels = Math.Clamp(Pixels(length, size), 1, size);
        var from = Math.Clamp(Pixels(start, size), 0, size - pixels);
        return (from, pixels);
    }

    private static MediaPixelRect Cut(MediaPixelRect area, MediaAspectRatio aspect, MediaFocalPoint? focal, int sourceWidth, int sourceHeight)
    {
        int width, height;
        if ((long)area.Width * aspect.Height >= (long)area.Height * aspect.Width)
        {
            height = area.Height;
            width = Math.Clamp((int)Math.Round((double)height * aspect.Width / aspect.Height, MidpointRounding.AwayFromZero), 1, area.Width);
        }
        else
        {
            width = area.Width;
            height = Math.Clamp((int)Math.Round((double)width * aspect.Height / aspect.Width, MidpointRounding.AwayFromZero), 1, area.Height);
        }

        var centreX = focal is null ? area.X + area.Width / 2.0 : (double)focal.X * sourceWidth;
        var centreY = focal is null ? area.Y + area.Height / 2.0 : (double)focal.Y * sourceHeight;
        var x = Place(centreX, width, area.X, area.Width);
        var y = Place(centreY, height, area.Y, area.Height);
        return new MediaPixelRect(x, y, width, height);
    }

    // The start of a span of the given length centred on a point, moved to stay inside the area.
    private static int Place(double centre, int length, int areaStart, int areaLength) =>
        Math.Clamp((int)Math.Round(centre - length / 2.0, MidpointRounding.AwayFromZero), areaStart, areaStart + areaLength - length);

    private static int Pixels(decimal fraction, int size) =>
        (int)Math.Round(fraction * size, MidpointRounding.AwayFromZero);
}
