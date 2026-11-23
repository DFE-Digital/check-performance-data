using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;

namespace DfE.CheckPerformanceData.Application.Media.RichText;

/// <summary>
/// Points the library images in rich text at the version that a page shows, when the page renders.
/// Rich text stores <c>&lt;img data-media-id="{id}" src="/media/{id}/..."&gt;</c> with the unsigned
/// original. Each one gets a signed responsive src, srcset and sizes, lazy loading and a size: the
/// width and height the editor set, else the size of the version shown. An SVG or an animation gets
/// its original, because it is never resized.
///
/// The input is HTML that the sanitiser has already cleaned, and the output is written straight to
/// the page, so this runs after the sanitiser and never before a save or a search index. An image
/// that is not from the library (a data: URI, another site) is left exactly as stored. An image that
/// is no longer in the library is dropped from a public page, and the editor and previews show a
/// warning in its place.
/// </summary>
public static class RichTextImageRewriter
{
    /// <summary>The sizes hint for an image the editor did not size: the width of the content column.</summary>
    public const string ColumnSizes = "(min-width: 1020px) 640px, (min-width: 641px) 66vw, 100vw";

    private const int DefaultSrcWidth = 960;
    private const string MissingImageText = "This image is no longer in the media library";

    /// <summary>The library images in <paramref name="html"/>, once each, in document order.</summary>
    public static IReadOnlyList<Guid> FindIds(string? html)
    {
        if (!MayHaveLibraryImages(html)) return [];
        return LibraryImages(new HtmlParser().ParseDocument(html!)).Select(i => i.Id).Distinct().ToList();
    }

    /// <param name="media">The images the page shows, by id (see <see cref="IMediaVersionResolver"/>).</param>
    /// <param name="showEditorNotices">True in the editor and previews: warn about a missing image.</param>
    public static string Rewrite(string html, IReadOnlyDictionary<Guid, ResolvedMedia> media, IMediaUrlBuilder urls,
        bool showEditorNotices)
    {
        if (!MayHaveLibraryImages(html)) return html;

        var document = new HtmlParser().ParseDocument(html);
        var images = LibraryImages(document).ToList();
        if (images.Count == 0) return html;

        foreach (var (img, id) in images)
        {
            if (media.TryGetValue(id, out var resolved))
                Point(img, resolved, urls);
            else if (showEditorNotices)
                img.Replace(MissingImageWarning(document));
            else
                img.Remove();
        }

        return document.Body!.InnerHtml;
    }

    /// <summary>
    /// Finds the version of each library image in <paramref name="html"/> for the page version, then
    /// rewrites the images. No lookup is made when the text has no library image.
    /// </summary>
    public static async Task<string> RenderRichTextImagesAsync(this IMediaVersionResolver resolver, string html,
        IMediaUrlBuilder urls, Guid? pageVersionId, bool showEditorNotices, CancellationToken ct = default)
    {
        var ids = FindIds(html);
        if (ids.Count == 0) return html;
        var media = await resolver.ResolveAsync(ids, pageVersionId, ct);
        return Rewrite(html, media, urls, showEditorNotices);
    }

    // A quick test that skips the parse for text with no library image, which is most rich text.
    private static bool MayHaveLibraryImages(string? html) =>
        !string.IsNullOrEmpty(html)
        && (html.Contains("data-media-id", StringComparison.OrdinalIgnoreCase)
            || html.Contains("/media/", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(IElement Img, Guid Id)> LibraryImages(IDocument document)
    {
        foreach (var img in document.QuerySelectorAll("img"))
        {
            if (MediaReferenceExtractor.TryGetImageId(img, out var id)) yield return (img, id);
        }
    }

    private static void Point(IElement img, ResolvedMedia media, IMediaUrlBuilder urls)
    {
        var (asset, version) = (media.Asset, media.Version);
        var (width, height) = Size(img, version);

        img.SetAttribute("data-media-id", asset.Id.ToString());
        img.RemoveAttribute("srcset");
        img.RemoveAttribute("sizes");
        if (MediaUrlBuilder.IsResizable(version))
        {
            var shown = Math.Min(width ?? DefaultSrcWidth, DefaultSrcWidth);
            var srcWidth = MediaRenditionQuery.AllowedWidths.FirstOrDefault(w => w >= shown, DefaultSrcWidth);
            img.SetAttribute("src", urls.Rendition(asset, version, srcWidth));
            img.SetAttribute("srcset", urls.SrcSet(asset, version));
            img.SetAttribute("sizes", HasNumber(img, "width")
                ? $"(max-width: {width}px) 100vw, {width}px"
                : ColumnSizes);
        }
        else
        {
            img.SetAttribute("src", urls.Original(asset, version));
        }

        // No alt attribute at all is a mistake, so the library's text fills it; an empty alt is the
        // editor's choice for a decorative image and stays empty.
        if (!img.HasAttribute("alt")) img.SetAttribute("alt", asset.DefaultAltText ?? "");
        if (width is { } w) img.SetAttribute("width", w.ToString(CultureInfo.InvariantCulture));
        if (height is { } h) img.SetAttribute("height", h.ToString(CultureInfo.InvariantCulture));
        // The page shrinks an image to its column with max-width and height:auto. Without a stated
        // shape the file's own shape then wins over the attributes, so a box the editor stretched
        // would show in the file's shape. The style is built here from two numbers, never copied.
        img.RemoveAttribute("style");
        if (width is { } sw && height is { } sh)
            img.SetAttribute("style", string.Create(CultureInfo.InvariantCulture, $"aspect-ratio: {sw} / {sh}"));
        img.SetAttribute("loading", "lazy");
        img.SetAttribute("decoding", "async");
    }

    // The size to write. A width and a height that the editor set both stay, also in another shape
    // than the file: the editor can unlock the ratio and stretch an image on purpose. With one side
    // only, the other side follows the shape of the version shown. With neither, the version's own
    // size. With no known shape, what is stored.
    private static (int? Width, int? Height) Size(IElement img, MediaAssetVersionDto version)
    {
        var storedWidth = Number(img, "width");
        var storedHeight = Number(img, "height");
        if (storedWidth is null && storedHeight is null && (img.HasAttribute("width") || img.HasAttribute("height")))
            return (null, null);
        return Box(storedWidth, storedHeight, version);
    }

    /// <summary>
    /// The box that a page gives a rich-text image, from the width and height that the editor set
    /// (each null when not set) and the version shown. Both sides stay; one side gives the other
    /// from the shape of the version; neither gives the size of the version.
    /// </summary>
    public static (int? Width, int? Height) Box(int? storedWidth, int? storedHeight, MediaAssetVersionDto version)
    {
        if (storedWidth is not null && storedHeight is not null) return (storedWidth, storedHeight);
        if (version.Width is not > 0 || version.Height is not > 0) return (storedWidth, storedHeight);

        var (vw, vh) = (version.Width.Value, version.Height.Value);
        if (storedWidth is { } w) return (w, (int)Math.Round(w * (double)vh / vw, MidpointRounding.AwayFromZero));
        if (storedHeight is { } h) return ((int)Math.Round(h * (double)vw / vh, MidpointRounding.AwayFromZero), h);
        return (vw, vh);
    }

    /// <summary>A whole number above 0 in the attribute, or null.</summary>
    public static int? SizeAttribute(IElement img, string attribute) => Number(img, attribute);

    private static bool HasNumber(IElement img, string attribute) => Number(img, attribute) is not null;

    private static int? Number(IElement img, string attribute) =>
        int.TryParse(img.GetAttribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;

    // GOV.UK warning text, built from inline elements so that it is valid inside a paragraph.
    private static IElement MissingImageWarning(IDocument document)
    {
        var warning = document.CreateElement("span");
        warning.ClassName = "govuk-warning-text";

        var icon = document.CreateElement("span");
        icon.ClassName = "govuk-warning-text__icon";
        icon.SetAttribute("aria-hidden", "true");
        icon.TextContent = "!";

        var text = document.CreateElement("strong");
        text.ClassName = "govuk-warning-text__text";
        var hidden = document.CreateElement("span");
        hidden.ClassName = "govuk-visually-hidden";
        hidden.TextContent = "Warning";
        text.AppendChild(hidden);
        text.AppendChild(document.CreateTextNode(MissingImageText));

        warning.AppendChild(icon);
        warning.AppendChild(text);
        return warning;
    }
}
