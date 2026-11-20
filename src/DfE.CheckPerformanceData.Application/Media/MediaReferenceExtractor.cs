using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Finds the library images that a page version's stored content uses, so the usages can be saved
/// with the version. A content page is a widget tree: a widget with a <c>mediaId</c> prop (the
/// Image widget) is a widget use, and an <c>&lt;img&gt;</c> in a prop that holds HTML is a rich-text
/// use. Any other body (a wiki page's markdown or HTML) is read as rich text. The result has one
/// reference for each image, location and widget, in document order.
/// </summary>
/// <remarks>
/// The extractor reads only the content. It does not check that an image exists: the caller drops
/// ids that are not in the library. It never throws, because a page save must not fail on content
/// it cannot read.
/// </remarks>
public static partial class MediaReferenceExtractor
{
    private const string MediaIdProp = "mediaId";
    private const string MediaIdAttribute = "data-media-id";

    // Widget props that hold HTML, by widget type. Other string props are plain text and the views
    // show them escaped, so an <img> written in them shows no image.
    private static readonly IReadOnlyDictionary<string, string[]> HtmlProps =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["richtext"] = ["html"],
        };

    public static IReadOnlyList<MediaReference> Extract(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var found = new List<MediaReference>();
        try
        {
            if (TryParseJson(content, out var json))
            {
                if (json is JsonArray tree) WalkColumn(tree, [], 0, found);
            }
            else
            {
                AddFromText(content, null, found);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Content that is not the expected shape gives what was found before the problem.
        }

        return found.Distinct().ToList();
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
            // Markdown can start with "[" (a link), so text that is not JSON is read as a body.
            return false;
        }
    }

    // One column of nodes: the page's top level (column 0) or one column of a region. Each step of
    // a widget's path names the column and the slot it sits in.
    private static void WalkColumn(JsonArray nodes, IReadOnlyList<TreeStep> parent, int column, List<MediaReference> found)
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

    private static void AddFromWidget(JsonObject widget, string path, List<MediaReference> found)
    {
        if (widget["props"] is not JsonObject props) return;

        if (TryGuid(StringValue(props[MediaIdProp]), out var mediaId))
            found.Add(new MediaReference(mediaId, MediaUsageLocations.Widget, path));

        if (StringValue(widget["type"]) is { } type && HtmlProps.TryGetValue(type, out var htmlProps))
        {
            foreach (var prop in htmlProps)
            {
                if (StringValue(props[prop]) is { Length: > 0 } html)
                    AddFromHtml(html, path, found);
            }
        }
    }

    private static void AddFromHtml(string html, string? path, List<MediaReference> found)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var img in document.QuerySelectorAll("img"))
        {
            // The data attribute names the image the editor chose, so it wins over the src.
            if (TryGuid(img.GetAttribute(MediaIdAttribute), out var id) || TryMediaPath(img.GetAttribute("src"), out id))
                found.Add(new MediaReference(id, MediaUsageLocations.RichText, path));
        }
    }

    // A body that is not a widget tree may be markdown, so its images are found by pattern rather
    // than as <img> elements: a data-media-id attribute or a /media/{id}/ address.
    private static void AddFromText(string text, string? path, List<MediaReference> found)
    {
        foreach (Match match in MediaInText().Matches(text))
        {
            var value = match.Groups["attr"].Success ? match.Groups["attr"].Value : match.Groups["url"].Value;
            if (Guid.TryParse(value, out var id))
                found.Add(new MediaReference(id, MediaUsageLocations.RichText, path));
        }
    }

    // "/media/{id}/..." on this site, or the same path on an absolute http(s) address (the library
    // hands out full links).
    private static bool TryMediaPath(string? src, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(src)) return false;
        src = src.Trim();

        string path;
        if (src.StartsWith('/') && !src.StartsWith("//", StringComparison.Ordinal))
            path = src;
        else if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            path = uri.AbsolutePath;
        else
            return false;

        var match = MediaPath().Match(path);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out id);
    }

    private static bool TryGuid(string? value, out Guid id)
    {
        id = Guid.Empty;
        return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out id) && id != Guid.Empty;
    }

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    [GeneratedRegex(@"^/media/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:/|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MediaPath();

    [GeneratedRegex(@"data-media-id\s*=\s*[""']?(?<attr>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})|(?:^|[\s(""'=])(?:https?://[^/\s""'()]+)?/media/(?<url>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MediaInText();
}
