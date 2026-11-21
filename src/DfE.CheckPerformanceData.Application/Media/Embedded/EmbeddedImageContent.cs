using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Finds the images embedded as <c>data:</c> URIs in a page version's content, and points them at
/// media library images. A content page is a widget tree, where only the props in
/// <see cref="MediaReferenceExtractor.HtmlProps"/> hold HTML. Any other body (a wiki page) is read
/// as rich text.
///
/// Only the tags change. Each embedded <c>&lt;img&gt;</c> gets the library src and a
/// <c>data-media-id</c>, as the rich-text editor saves a library image. Every other attribute, and
/// every character outside those tags, stays exactly as stored. So a body that mixes markdown and
/// HTML is not reformatted, and the page looks the same after the move.
/// </summary>
public static partial class EmbeddedImageContent
{
    private const string MediaIdAttribute = "data-media-id";

    /// <summary>The embedded images in the content, in document order. An image used twice is listed twice.</summary>
    public static IReadOnlyList<EmbeddedImage> Find(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || !MayHaveDataUri(content)) return [];

        if (TryParseJson(content, out var json))
        {
            var found = new List<EmbeddedImage>();
            foreach (var (props, prop) in HtmlValues(json!))
                found.AddRange(FindInHtml(props[prop]!.GetValue<string>()));
            return found;
        }

        return FindInHtml(content);
    }

    /// <summary>
    /// The embedded images in rich text that is never a widget tree, such as a content block's
    /// value. Text that starts like JSON is still read as rich text.
    /// </summary>
    public static IReadOnlyList<EmbeddedImage> FindInRichText(string? content) =>
        string.IsNullOrWhiteSpace(content) || !MayHaveDataUri(content) ? [] : FindInHtml(content);

    /// <summary>As <see cref="Replace"/>, for rich text that is never a widget tree.</summary>
    public static string ReplaceInRichText(string content, IReadOnlyDictionary<string, LibraryImageSrc> bySrc) =>
        bySrc.Count == 0 || string.IsNullOrWhiteSpace(content) || !MayHaveDataUri(content)
            ? content
            : ReplaceInHtml(content, bySrc);

    /// <summary>
    /// Points each embedded image whose src is a key of <paramref name="bySrc"/> at that library
    /// image. Others are left as they are. Returns the same string when nothing changes.
    /// </summary>
    public static string Replace(string content, IReadOnlyDictionary<string, LibraryImageSrc> bySrc)
    {
        if (bySrc.Count == 0 || string.IsNullOrWhiteSpace(content) || !MayHaveDataUri(content)) return content;

        if (!TryParseJson(content, out var json)) return ReplaceInHtml(content, bySrc);

        var changed = false;
        foreach (var (props, prop) in HtmlValues(json!).ToList())
        {
            var html = props[prop]!.GetValue<string>();
            var replaced = ReplaceInHtml(html, bySrc);
            if (ReferenceEquals(replaced, html)) continue;
            props[prop] = replaced;
            changed = true;
        }

        // Written as the content editor writes a tree, so the stored format does not change.
        return changed ? json!.ToJsonString(ContentPageJson.Options) : content;
    }

    private static bool MayHaveDataUri(string content) => content.Contains("data:", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseJson(string content, out JsonNode? json)
    {
        json = null;
        var trimmed = content.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '[' && trimmed[0] != '{')) return false;
        try
        {
            json = JsonNode.Parse(content);
            return json is not null;
        }
        catch (JsonException)
        {
            // Markdown can start with "[" (a link), so text that is not JSON is read as a body.
            return false;
        }
    }

    // Every HTML prop of every widget in the tree, regions and their columns included.
    private static IEnumerable<(JsonObject Props, string Prop)> HtmlValues(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child is null) continue;
                    foreach (var value in HtmlValues(child)) yield return value;
                }
                break;
            case JsonObject obj:
                if (StringValue(obj["kind"]) == "widget"
                    && StringValue(obj["type"]) is { } type
                    && MediaReferenceExtractor.HtmlProps.TryGetValue(type, out var htmlProps)
                    && obj["props"] is JsonObject props)
                {
                    foreach (var prop in htmlProps)
                    {
                        if (StringValue(props[prop]) is { Length: > 0 }) yield return (props, prop);
                    }
                }
                foreach (var (_, child) in obj)
                {
                    if (child is JsonArray or JsonObject)
                    {
                        foreach (var value in HtmlValues(child)) yield return value;
                    }
                }
                break;
        }
    }

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // ── One piece of HTML ────────────────────────────────────────────────────

    private static List<EmbeddedImage> FindInHtml(string html)
    {
        var found = new List<EmbeddedImage>();
        foreach (Match tag in ImgTag().Matches(html))
        {
            var attributes = Attributes(tag.Value);
            if (EmbeddedSrc(attributes) is not { } src) continue;
            found.Add(new EmbeddedImage(src.Value, Value(attributes, "alt"), Value(attributes, "title")));
        }
        return found;
    }

    private static string ReplaceInHtml(string html, IReadOnlyDictionary<string, LibraryImageSrc> bySrc)
    {
        StringBuilder? result = null;
        var copied = 0;
        foreach (Match tag in ImgTag().Matches(html))
        {
            var attributes = Attributes(tag.Value);
            if (EmbeddedSrc(attributes) is not { } src || !bySrc.TryGetValue(src.Value, out var image)) continue;

            result ??= new StringBuilder(html.Length);
            result.Append(html, copied, tag.Index - copied);
            result.Append(RewriteTag(tag.Value, attributes, src, image));
            copied = tag.Index + tag.Length;
        }

        if (result is null) return html;
        result.Append(html, copied, html.Length - copied);
        return result.ToString();
    }

    // The tag with its src pointed at the library image and a data-media-id after it. Any old
    // data-media-id is dropped. Edits run from the end, so earlier positions stay right.
    private static string RewriteTag(string tag, IReadOnlyList<TagAttribute> attributes, TagAttribute src, LibraryImageSrc image)
    {
        var builder = new StringBuilder(tag);
        foreach (var attribute in attributes.OrderByDescending(a => a.Index))
        {
            if (ReferenceEquals(attribute, src))
            {
                builder.Remove(attribute.Index, attribute.Length);
                builder.Insert(attribute.Index,
                    $"{attribute.Space}src=\"{WebUtility.HtmlEncode(image.Src)}\" {MediaIdAttribute}=\"{image.Id}\"");
            }
            else if (attribute.Name.Equals(MediaIdAttribute, StringComparison.OrdinalIgnoreCase))
            {
                builder.Remove(attribute.Index, attribute.Length);
            }
        }
        return builder.ToString();
    }

    private sealed record TagAttribute(string Name, string? Value, string Space, int Index, int Length);

    private static List<TagAttribute> Attributes(string tag)
    {
        // Skip "<img" so the element name is not read as an attribute.
        const int nameLength = 4;
        var attributes = new List<TagAttribute>();
        foreach (Match m in Attribute().Matches(tag, nameLength))
        {
            var value = m.Groups["value"].Success ? WebUtility.HtmlDecode(m.Groups["value"].Value) : null;
            attributes.Add(new TagAttribute(m.Groups["name"].Value, value, m.Groups["space"].Value, m.Index, m.Length));
        }
        return attributes;
    }

    // The first src wins, as in a browser.
    private static TagAttribute? EmbeddedSrc(List<TagAttribute> attributes)
    {
        var src = attributes.FirstOrDefault(a => a.Name.Equals("src", StringComparison.OrdinalIgnoreCase));
        return src?.Value is { } value && value.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? src : null;
    }

    private static string? Value(List<TagAttribute> attributes, string name) =>
        attributes.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    // An <img> start tag. A quoted value may hold ">", as an inline SVG does.
    [GeneratedRegex("""<img\b(?:[^>"']|"[^"]*"|'[^']*')*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImgTag();

    // One attribute with the space before it. An unquoted value runs to the next space or ">".
    [GeneratedRegex("""(?<space>\s*)(?<name>[^\s"'>/=]+)(?:\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'>]+)))?""",
        RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();
}
