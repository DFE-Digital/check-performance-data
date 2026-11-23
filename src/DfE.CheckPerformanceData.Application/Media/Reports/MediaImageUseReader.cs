using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media.RichText;
using DfE.CheckPerformanceData.Application.Media.Variants;
using DfE.CheckPerformanceData.Application.Media.Widget;

namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// Reads each use of a library image from stored content, with the alt text and caption that the
/// content gives it. It finds images as <see cref="MediaReferenceExtractor"/> does: an Image
/// widget by its mediaId prop, and an <c>&lt;img&gt;</c> in a widget prop that holds HTML. A body
/// that is not a widget tree is read as HTML; a markdown image is not an <c>&lt;img&gt;</c> and is
/// not read. Unlike the extractor, it keeps every use, because two uses of one image can have
/// different alt text or a different crop or size. It never throws.
/// </summary>
/// <remarks>
/// The alt text follows the renderers. An Image widget's blank alt prop shows the library's text
/// (null here). A rich-text img with no alt attribute shows the library's text too (null). An img
/// with an empty alt and <c>role="presentation"</c> or <c>role="none"</c> is decorative: the
/// editor's "decorative" option writes both. An empty alt with no such role is empty text: the
/// editor warns about it, so the report does too.
/// </remarks>
public static class MediaImageUseReader
{
    public static IReadOnlyList<MediaImageUse> Read(string? content) => Walk(content).Select(f => f.Use).ToList();

    /// <summary>The library images in a rich-text body that is never a widget tree, such as a content block's value.</summary>
    public static IReadOnlyList<MediaImageUse> ReadRichText(string? html) => WalkRichText(html).Select(f => f.Use).ToList();

    /// <summary>
    /// Each use with the crop, aspect ratio and focal point, or the rich-text width and height,
    /// that the page uses to show it. A setting that the page would ignore or refuse is null.
    /// </summary>
    public static IReadOnlyList<MediaImageShapeUse> ReadShapes(string? content) => Walk(content).Select(f => f.Shape).ToList();

    /// <summary>The shapes of the images in a rich-text body that is never a widget tree.</summary>
    public static IReadOnlyList<MediaImageShapeUse> ReadRichTextShapes(string? html) => WalkRichText(html).Select(f => f.Shape).ToList();

    private sealed record Found(MediaImageUse Use, MediaImageShapeUse Shape);

    private static List<Found> Walk(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var found = new List<Found>();
        try
        {
            if (TryParseJson(content, out var json))
            {
                if (json is JsonArray tree) WalkColumn(tree, [], 0, found);
            }
            else
            {
                AddFromHtml(content, null, found);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Content that is not the expected shape gives what was found before the problem.
        }
        return found;
    }

    private static List<Found> WalkRichText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var found = new List<Found>();
        AddFromHtml(html, null, found);
        return found;
    }

    private static bool TryParseJson(string content, out JsonNode? json)
    {
        json = null;
        var trimmed = content.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '[' && trimmed[0] != '{')) return false;
        try
        {
            json = JsonNode.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WalkColumn(JsonArray nodes, IReadOnlyList<TreeStep> parent, int column, List<Found> found)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JsonObject node) continue;
            List<TreeStep> path = [.. parent, new TreeStep(column, index)];

            switch (StringValue(node["kind"]))
            {
                case "widget":
                    AddFromWidget(node, TreePath.Format(path), found);
                    break;
                case "region" when node["columns"] is JsonArray columns:
                    for (var c = 0; c < columns.Count; c++)
                    {
                        if (columns[c] is JsonArray children)
                            WalkColumn(children, path, c, found);
                    }
                    break;
            }
        }
    }

    private static void AddFromWidget(JsonObject widget, string path, List<Found> found)
    {
        if (widget["props"] is not JsonObject props) return;

        if (ImageWidgetProps.TryMediaId(props, out var mediaId))
        {
            var use = new MediaImageUse(
                mediaId,
                MediaUsageLocations.Widget,
                path,
                NullIfBlank(ImageWidgetProps.Text(props, ImageWidgetProps.Alt)),
                ImageWidgetProps.IsDecorative(props),
                NullIfBlank(ImageWidgetProps.Text(props, ImageWidgetProps.Caption)));
            var aspect = ImageWidgetProps.ParseAspect(ImageWidgetProps.Text(props, ImageWidgetProps.Aspect));
            // A focal point only places an aspect-ratio cut, so the page drops it when there is no ratio.
            var focal = aspect is null ? null : ImageWidgetProps.ParseFocal(ImageWidgetProps.Text(props, ImageWidgetProps.Focal));
            var shape = new MediaImageShapeUse(mediaId, MediaUsageLocations.Widget, path,
                ImageWidgetProps.ParseCrop(ImageWidgetProps.Text(props, ImageWidgetProps.Crop)), aspect, focal, null, null);
            found.Add(new Found(use, shape));
        }

        if (StringValue(widget["type"]) is { } type && MediaReferenceExtractor.HtmlProps.TryGetValue(type, out var htmlProps))
        {
            foreach (var prop in htmlProps)
            {
                if (StringValue(props[prop]) is { Length: > 0 } html)
                    AddFromHtml(html, path, found);
            }
        }
    }

    private static void AddFromHtml(string html, string? path, List<Found> found)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var img in document.QuerySelectorAll("img"))
        {
            if (!MediaReferenceExtractor.TryGetImageId(img, out var id)) continue;
            found.Add(new Found(
                new MediaImageUse(id, MediaUsageLocations.RichText, path, img.GetAttribute("alt"), IsDecorative(img), FigureCaption(img)),
                new MediaImageShapeUse(id, MediaUsageLocations.RichText, path, null, null, null,
                    RichTextImageRewriter.SizeAttribute(img, "width"), RichTextImageRewriter.SizeAttribute(img, "height"))));
        }
    }

    // The editor's "decorative" option writes both. Without the alt attribute the page shows the
    // library's text, so the image is not decorative there.
    private static bool IsDecorative(IElement img) =>
        img.GetAttribute("role")?.Trim().ToLowerInvariant() is "presentation" or "none"
        && img.GetAttribute("alt") is { } alt && alt.Trim().Length == 0;

    // The caption of a <figure> that holds the image, as readers see it next to the image.
    private static string? FigureCaption(IElement img) =>
        NullIfBlank(img.Closest("figure")?.QuerySelector("figcaption")?.TextContent);

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
