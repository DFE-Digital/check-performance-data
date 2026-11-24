using System.Globalization;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// The words for one way that a page shows an image: a short title, and the settings one to a
/// line. Fractions show as percentages of the original. A focal point is also named by the part of
/// the image it is in: "left", "centre" or "right" across, and "top", "centre" or "bottom" down.
/// </summary>
public static class MediaVariantWording
{
    // A focal point less than a third of the way across is "left", more than two thirds is "right".
    private const decimal Near = 0.33m;
    private const decimal Far = 0.67m;

    public static MediaVariantDescription Describe(MediaVariantShape shape, MediaAssetVersionDto version)
    {
        if (shape.IsRichTextBox) return Box(shape, version);
        if (shape.Cut is { } cut) return Cut(cut, version);

        var size = version is { Width: { } w, Height: { } h } ? $"The whole image, {w} × {h} pixels." : "The whole image.";
        return new MediaVariantDescription("Original, not cropped", [size]);
    }

    private static MediaVariantDescription Cut(MediaRendition cut, MediaAssetVersionDto version)
    {
        var parts = new List<string>();
        var details = new List<string>();

        if (cut.Crop is { } crop)
        {
            parts.Add("Cropped to part of the image");
            details.Add($"Area used: {Percent(crop.X)} to {Percent(crop.X + crop.Width)} across, {Percent(crop.Y)} to {Percent(crop.Y + crop.Height)} down.");
        }

        if (cut.Aspect is { } aspect)
        {
            var ratio = $"{aspect.Width}:{aspect.Height}";
            var where = cut.Focal is { } focal ? $"focal point {Place(focal)}" : "from the centre";
            parts.Add(parts.Count == 0 ? $"Cropped to {ratio}, {where}" : $"then to {ratio}, {where}");
            details.Add($"Shape: {ratio}.");
            details.Add(cut.Focal is { } f
                ? $"Focal point: {Percent(f.X)} across, {Percent(f.Y)} down."
                : "Focal point: not set, so the middle of the area is kept.");
        }

        if (version is { Width: > 0, Height: > 0 })
        {
            var layout = MediaRenditionGeometry.Compute(version.Width.Value, version.Height.Value, cut);
            details.Add($"Shows {layout.Width} × {layout.Height} pixels of the original.");
        }

        return new MediaVariantDescription(string.Join(", ", parts), details);
    }

    private static MediaVariantDescription Box(MediaVariantShape shape, MediaAssetVersionDto version)
    {
        var title = (shape.BoxWidth, shape.BoxHeight) switch
        {
            ({ } w, { } h) => $"Shown at {w} × {h} in rich text",
            ({ } w, null) => $"Shown {w} pixels wide in rich text",
            (null, { } h) => $"Shown {h} pixels tall in rich text",
            _ => "Shown in rich text",
        };

        string detail;
        if (shape is { BoxWidth: { } bw, BoxHeight: { } bh } && version is { Width: > 0, Height: > 0 })
        {
            // The same shape when the sides differ by less than one pixel after scaling.
            var sameShape = Math.Abs(bw * (double)version.Height.Value / version.Width.Value - bh) < 1;
            detail = sameShape
                ? "The whole image, scaled to this size."
                : "The box is not the shape of the image, so the image is stretched to fill it.";
        }
        else
        {
            detail = "The whole image. The other side follows the shape of the image.";
        }

        return new MediaVariantDescription(title, [detail, "The page sets this size on the image. It is not a crop."]);
    }

    private static string Place(MediaFocalPoint focal)
    {
        var across = focal.X < Near ? "left" : focal.X > Far ? "right" : "centre";
        var down = focal.Y < Near ? "top" : focal.Y > Far ? "bottom" : "centre";
        return (down, across) switch
        {
            ("centre", "centre") => "centre",
            _ => $"{down}-{across}",
        };
    }

    private static string Percent(decimal fraction) =>
        Math.Round(fraction * 100m, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
