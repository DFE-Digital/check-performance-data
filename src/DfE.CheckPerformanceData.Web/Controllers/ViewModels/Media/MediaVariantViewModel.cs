using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Variants;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// One way that current content shows an image, as the image page lists it under "Crops and
/// sizes": the words, a preview made from the current file through the signed rendition URLs, and
/// the pages and blocks that show it this way. A crop is made from the original when a page is
/// shown, so the preview is too. A rich-text box is shown by the size of the img, as on the page,
/// because a rendition never stretches an image.
/// </summary>
public sealed record MediaVariantViewModel(
    string Title,
    IReadOnlyList<string> Details,
    bool IsOriginal,
    string PreviewUrl,
    string PreviewAlt,
    int? PreviewWidth,
    int? PreviewHeight,
    IReadOnlyList<MediaVariantUseViewModel> Uses)
{
    /// <summary>The id of the "Crops and sizes" section on the image page.</summary>
    public const string SectionId = "crops-and-sizes";

    /// <summary>The width of the rendition behind a preview: twice the box, for sharp screens.</summary>
    public const int PreviewRenditionWidth = 640;

    /// <summary>The widest a preview shows on the page.</summary>
    public const int PreviewMaxWidth = 320;

    public static MediaVariantViewModel From(MediaAssetDto asset, MediaVariant variant, IMediaUrlBuilder urls)
    {
        var version = asset.Latest;
        var shape = variant.Shape;
        var words = MediaVariantWording.Describe(shape, version);

        var url = shape.Cut is { } cut
            ? urls.Rendition(asset, version, PreviewRenditionWidth, cut.Crop, cut.Aspect, cut.Focal)
            : urls.Rendition(asset, version, PreviewRenditionWidth);
        var (width, height) = Fit(Shown(shape, version));

        return new MediaVariantViewModel(
            words.Title,
            words.Details,
            shape.IsOriginal,
            url,
            $"{asset.FileName}, {char.ToLowerInvariant(words.Title[0])}{words.Title[1..]}",
            width,
            height,
            variant.Uses.Select(MediaVariantUseViewModel.From).ToList());
    }

    // The size that the page shows: the file, the cut area, or the rich-text box.
    private static (int? Width, int? Height) Shown(MediaVariantShape shape, MediaAssetVersionDto version)
    {
        if (shape.IsRichTextBox) return (shape.BoxWidth, shape.BoxHeight);
        if (version is not { Width: > 0, Height: > 0 }) return (null, null);
        if (shape.Cut is { } cut && MediaUrlBuilder.IsResizable(version))
        {
            var layout = MediaRenditionGeometry.Compute(version.Width.Value, version.Height.Value, cut);
            return (layout.Width, layout.Height);
        }
        return (version.Width, version.Height);
    }

    private static (int? Width, int? Height) Fit((int? Width, int? Height) size)
    {
        if (size is not ({ } w and > 0, { } h and > 0)) return (null, null);
        if (w <= PreviewMaxWidth) return (w, h);
        return (PreviewMaxWidth, Math.Max(1, (int)Math.Round(h * (double)PreviewMaxWidth / w, MidpointRounding.AwayFromZero)));
    }
}

/// <summary>A page or a content block that shows an image one way, with a link to its editor.</summary>
public sealed record MediaVariantUseViewModel(string Name, string Url, bool IsBlock, string Where, string StateText)
{
    public static MediaVariantUseViewModel From(MediaVariantUse use)
    {
        var where = use.Location == MediaUsageLocations.Widget ? "Image widget" : "Rich text";
        if (use.Count > 1) where += $", {use.Count} times";

        if (use.ContentBlockId is not null)
        {
            var key = Uri.EscapeDataString(use.Name);
            return new MediaVariantUseViewModel(use.Name, $"/admin/content-blocks?edit={key}#edit-{key}", true, where, "Content block");
        }

        return new MediaVariantUseViewModel(use.Name, $"/admin/pages/{use.PageNodeId}", false, where,
            string.Join(", ", use.States.Select(s => s.ToString())));
    }
}
