using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// Works out what the Image widget writes, from its stored props and the image version the page
/// shows. A static image gets signed WebP copies: a src at a default width for its grid width, a
/// srcset of the fixed widths and a sizes hint from the GOV.UK grid (960px page, 641px tablet
/// break, 1020px desktop break). Width and height are the shape shown after the crop and aspect
/// ratio, so the page does not move as the image loads.
///
/// An SVG or an animation is always served as stored, so it gets the original URL, no srcset,
/// and its own size; the crop and aspect ratio do not apply to it.
///
/// Stored props can come from an import that skipped the save check, so a value that would be
/// refused is left out, and an unsafe link is dropped. This never throws for stored props.
/// </summary>
public static class ImageWidgetViewBuilder
{
    private static readonly IReadOnlyDictionary<string, (int SrcWidth, string Sizes)> ByWidth =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["full"] = (960, "(min-width: 1020px) 960px, 100vw"),
            ["two-thirds"] = (640, "(min-width: 1020px) 640px, (min-width: 641px) 67vw, 100vw"),
            ["one-half"] = (640, "(min-width: 1020px) 480px, (min-width: 641px) 50vw, 100vw"),
            ["one-third"] = (320, "(min-width: 1020px) 320px, (min-width: 641px) 34vw, 100vw"),
        };

    public static ImageWidgetView Build(WidgetNode widget, ResolvedMedia media, IMediaUrlBuilder urls)
    {
        var props = widget.Props;
        var (asset, version) = (media.Asset, media.Version);

        var width = ImageWidgetProps.Text(props, ImageWidgetProps.Width);
        if (!ByWidth.ContainsKey(width)) width = ImageWidgetProps.FullWidth;
        var align = ImageWidgetProps.Text(props, ImageWidgetProps.Align);
        var css = $"app-image app-image--{width}";
        if (align.Length > 0 && ImageWidgetProps.Aligns.Contains(align, StringComparer.Ordinal)) css += $" app-image--align-{align}";

        var alt = ImageWidgetProps.IsDecorative(props) ? "" : AltText(props, asset);
        var href = ImageWidgetProps.Text(props, ImageWidgetProps.Href);
        var caption = NullIfBlank(ImageWidgetProps.Text(props, ImageWidgetProps.Caption));
        var credit = NullIfBlank(ImageWidgetProps.Text(props, ImageWidgetProps.Credit));
        var link = SafeLink.IsSafe(href) ? href : null;

        if (!MediaUrlBuilder.IsResizable(version))
            return new ImageWidgetView(urls.Original(asset, version), null, null, version.Width, version.Height,
                alt, css, caption, credit, link);

        var crop = ImageWidgetProps.ParseCrop(ImageWidgetProps.Text(props, ImageWidgetProps.Crop));
        var aspect = ImageWidgetProps.ParseAspect(ImageWidgetProps.Text(props, ImageWidgetProps.Aspect));
        // A focal point only places an aspect-ratio cut, so it is dropped when there is no ratio.
        var focal = aspect is null ? null : ImageWidgetProps.ParseFocal(ImageWidgetProps.Text(props, ImageWidgetProps.Focal));

        var (srcWidth, sizes) = ByWidth[width];
        var shape = MediaRenditionQuery.Normalise(null, crop, aspect, focal);
        var shown = MediaRenditionGeometry.Compute(version.Width!.Value, version.Height!.Value, shape);

        return new ImageWidgetView(
            urls.Rendition(asset, version, srcWidth, crop, aspect, focal),
            urls.SrcSet(asset, version, crop, aspect, focal),
            sizes,
            shown.Width,
            shown.Height,
            alt, css, caption, credit, link);
    }

    // The page's own alt text, or the library's when the page has none.
    private static string AltText(System.Text.Json.Nodes.JsonObject? props, MediaAssetDto asset)
    {
        var alt = ImageWidgetProps.Text(props, ImageWidgetProps.Alt);
        return string.IsNullOrWhiteSpace(alt) ? asset.DefaultAltText ?? "" : alt;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
