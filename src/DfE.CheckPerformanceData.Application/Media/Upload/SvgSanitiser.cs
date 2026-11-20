using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Rebuilds an uploaded SVG from an allow-list, so a browser can show it but never run anything
/// from it or load anything else through it.
///
/// The file is streamed through an XmlReader into a new document; nothing is copied unless it is
/// on the lists below. DTDs (and so entities, entity bombs and XXE) make the file unreadable, as do
/// a broken structure, a root that is not an SVG svg element, more than
/// <see cref="MediaUploadLimits.MaxSvgCharacters"/> characters or nesting deeper than
/// <see cref="MediaUploadLimits.MaxSvgDepth"/>. Processing instructions (xml-stylesheet) and
/// comments are dropped. Elements outside the SVG namespace or the list go with their whole
/// subtree, which removes script and foreignObject. Attributes outside the list go, which removes
/// every on* handler. A link (href or xlink:href) may only point inside the document (#id), or,
/// on image, be a PNG, JPEG, GIF or WebP data URL. Any value with javascript:, vbscript:, another
/// data: URL, expression(), @import, image-set() or a url() that is not #id is dropped. CSS is checked after
/// its comments and backslash escapes are decoded: one bad declaration is dropped from a style
/// attribute, and a style element with any bad content is dropped whole. An animation is dropped
/// unless it animates a listed attribute other than href, with safe from/to/by/values.
/// </summary>
public static partial class SvgSanitiser
{
    private const string SvgNs = "http://www.w3.org/2000/svg";
    private const string XlinkNs = "http://www.w3.org/1999/xlink";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";

    private static readonly HashSet<string> AllowedElements =
    [
        "svg", "g", "defs", "symbol", "use", "switch", "view", "title", "desc", "a", "style",
        "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "image",
        "text", "tspan", "textPath",
        "linearGradient", "radialGradient", "stop", "pattern", "clipPath", "mask", "marker",
        "filter", "feBlend", "feColorMatrix", "feComponentTransfer", "feComposite", "feConvolveMatrix",
        "feDiffuseLighting", "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood",
        "feFuncA", "feFuncB", "feFuncG", "feFuncR", "feGaussianBlur", "feMerge", "feMergeNode",
        "feMorphology", "feOffset", "fePointLight", "feSpecularLighting", "feSpotLight", "feTile",
        "feTurbulence",
        "animate", "animateMotion", "animateTransform", "set", "mpath"
    ];

    private static readonly HashSet<string> AllowedAttributes =
    [
        // Core and accessibility.
        "id", "class", "style", "lang", "type", "media", "role", "focusable", "systemLanguage",
        "aria-label", "aria-labelledby", "aria-describedby", "aria-hidden",
        // Geometry and layout.
        "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy", "fr",
        "width", "height", "d", "points", "pathLength", "transform", "viewBox", "preserveAspectRatio",
        "version", "baseProfile",
        // Presentation.
        "alignment-baseline", "baseline-shift", "clip", "clip-path", "clip-rule", "color",
        "color-interpolation", "color-interpolation-filters", "color-rendering", "direction", "display",
        "dominant-baseline", "fill", "fill-opacity", "fill-rule", "filter", "flood-color", "flood-opacity",
        "font", "font-family", "font-size", "font-size-adjust", "font-stretch", "font-style", "font-variant",
        "font-weight", "image-rendering", "isolation", "letter-spacing", "lighting-color", "marker",
        "marker-start", "marker-mid", "marker-end", "mask", "mix-blend-mode", "opacity", "overflow",
        "paint-order", "pointer-events", "shape-rendering", "stop-color", "stop-opacity", "stroke",
        "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
        "stroke-opacity", "stroke-width", "text-anchor", "text-decoration", "text-rendering",
        "transform-origin", "unicode-bidi", "vector-effect", "visibility", "word-spacing", "writing-mode",
        // Gradients, patterns, clips, masks and markers.
        "href", "gradientUnits", "gradientTransform", "spreadMethod", "offset", "patternUnits",
        "patternContentUnits", "patternTransform", "clipPathUnits", "maskUnits", "maskContentUnits",
        "markerWidth", "markerHeight", "markerUnits", "refX", "refY", "orient",
        // Text.
        "dx", "dy", "rotate", "textLength", "lengthAdjust", "startOffset", "method", "spacing", "side",
        // Filters.
        "filterUnits", "primitiveUnits", "in", "in2", "result", "stdDeviation", "mode", "operator",
        "k1", "k2", "k3", "k4", "values", "tableValues", "slope", "intercept", "amplitude", "exponent",
        "order", "kernelMatrix", "divisor", "bias", "targetX", "targetY", "edgeMode", "kernelUnitLength",
        "preserveAlpha", "surfaceScale", "diffuseConstant", "specularConstant", "specularExponent", "scale",
        "xChannelSelector", "yChannelSelector", "radius", "baseFrequency", "numOctaves", "seed",
        "stitchTiles", "azimuth", "elevation", "z", "pointsAtX", "pointsAtY", "pointsAtZ",
        "limitingConeAngle",
        // Animation timing and values.
        "attributeName", "attributeType", "begin", "dur", "end", "min", "max", "restart", "repeatCount",
        "repeatDur", "calcMode", "keyTimes", "keySplines", "keyPoints", "from", "to", "by", "additive",
        "accumulate", "path"
    ];

    // Elements on which a link to another part of the same document is meaningful.
    private static readonly HashSet<string> LinkingElements =
        ["a", "use", "image", "textPath", "mpath", "linearGradient", "radialGradient", "pattern", "filter"];

    private static readonly HashSet<string> TransformAttributes = ["transform", "gradientTransform", "patternTransform"];

    private static readonly string[] ForbiddenCss =
        ["javascript:", "vbscript:", "data:", "expression(", "@import", "image-set(", "-moz-binding", "behavior:"];

    private sealed record Attribute(string Prefix, string LocalName, string Namespace, string Value);

    public static MediaCheckResult Sanitise(byte[] bytes)
    {
        try
        {
            return MediaCheckResult.Accept(Rebuild(bytes));
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return MediaCheckResult.Reject(MediaRejections.SvgUnreadable);
        }
    }

    private static CleanedMediaFile Rebuild(byte[] bytes)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MediaUploadLimits.MaxSvgCharacters,
            MaxCharactersFromEntities = 1,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
            NamespaceHandling = NamespaceHandling.OmitDuplicates
        };

        using var input = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(input, readerSettings);
        using var output = new MemoryStream();

        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg" || reader.NamespaceURI != SvgNs)
            throw new InvalidDataException("The root element is not an SVG svg element.");
        var (width, height) = ReadSize(reader);

        using (var writer = XmlWriter.Create(output, writerSettings))
        {
            CopyDocument(reader, writer);
        }
        // Reading on to the end makes the reader report anything malformed after the root.
        while (reader.Read()) { }

        return new CleanedMediaFile(output.ToArray(), MediaFormat.Svg, width, height, IsAnimated: false);
    }

    private static void CopyDocument(XmlReader reader, XmlWriter writer)
    {
        while (!reader.EOF)
        {
            if (reader.Depth > MediaUploadLimits.MaxSvgDepth)
                throw new InvalidDataException("The SVG is nested too deeply.");

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (!IsAllowedElement(reader))
                    {
                        reader.Skip();
                        continue;
                    }
                    var element = reader.LocalName;
                    var attributes = SafeAttributes(reader, element);
                    if (element == "style")
                    {
                        // Moves the reader past the element, so the loop must not read again.
                        var css = reader.ReadElementContentAsString();
                        if (IsSafeCss(css))
                        {
                            WriteStart(writer, element, attributes);
                            writer.WriteString(css);
                            writer.WriteEndElement();
                        }
                        continue;
                    }
                    WriteStart(writer, element, attributes);
                    if (reader.IsEmptyElement) writer.WriteEndElement();
                    if (reader.IsEmptyElement && reader.Depth == 0) return;
                    break;

                case XmlNodeType.EndElement:
                    writer.WriteEndElement();
                    if (reader.Depth == 0) return;
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    writer.WriteString(reader.Value);
                    break;
            }
            reader.Read();
        }
    }

    private static void WriteStart(XmlWriter writer, string element, List<Attribute> attributes)
    {
        writer.WriteStartElement("", element, SvgNs);
        foreach (var a in attributes)
        {
            if (a.Namespace.Length == 0) writer.WriteAttributeString(a.LocalName, a.Value);
            else writer.WriteAttributeString(a.Prefix, a.LocalName, a.Namespace, a.Value);
        }
    }

    private static bool IsAllowedElement(XmlReader reader)
    {
        if (reader.NamespaceURI != SvgNs || !AllowedElements.Contains(reader.LocalName)) return false;
        return reader.LocalName switch
        {
            "animate" or "set" => IsSafeAnimation(reader, name => IsAnimatable(name)),
            "animateTransform" => IsSafeAnimation(reader, TransformAttributes.Contains),
            "animateMotion" => IsSafeAnimation(reader, _ => true),
            _ => true
        };
    }

    private static bool IsAnimatable(string name) =>
        AllowedAttributes.Contains(name) && name != "href" && name != "attributeName";

    private static bool IsSafeAnimation(XmlReader reader, Func<string, bool> mayAnimate)
    {
        if (reader.LocalName != "animateMotion")
        {
            var target = reader.GetAttribute("attributeName")?.Trim();
            if (string.IsNullOrEmpty(target) || !mayAnimate(target)) return false;
        }
        foreach (var name in (string[])["from", "to", "by", "values", "path"])
        {
            var value = reader.GetAttribute(name);
            if (value is not null && !IsSafeValue(value)) return false;
        }
        return true;
    }

    private static List<Attribute> SafeAttributes(XmlReader reader, string element)
    {
        var kept = new List<Attribute>();
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                var attribute = SafeAttribute(reader, element);
                if (attribute is not null) kept.Add(attribute);
            }
            while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        return kept;
    }

    private static Attribute? SafeAttribute(XmlReader reader, string element)
    {
        var (ns, name, value) = (reader.NamespaceURI, reader.LocalName, reader.Value);

        if (ns == XmlnsNs) return null; // the writer declares the namespaces it uses
        if (ns == XmlNs) return name is "space" or "lang" ? new Attribute("xml", name, XmlNs, value) : null;
        if (ns == XlinkNs)
            return name == "href" && IsSafeHref(element, value) ? new Attribute("xlink", "href", XlinkNs, value) : null;
        if (ns.Length != 0 || !AllowedAttributes.Contains(name)) return null;

        if (name == "href") return IsSafeHref(element, value) ? new Attribute("", name, "", value) : null;
        if (name == "style")
        {
            var style = SafeStyleAttribute(value);
            return style.Length == 0 ? null : new Attribute("", name, "", style);
        }
        return IsSafeValue(value) ? new Attribute("", name, "", value) : null;
    }

    private static bool IsSafeHref(string element, string value)
    {
        if (!LinkingElements.Contains(element)) return false;
        var compact = Compact(value);
        if (compact.StartsWith('#')) return IsSafeValue(value);
        return element == "image" && RasterDataUrl().IsMatch(compact);
    }

    // Keeps each declaration of a style attribute that is safe on its own.
    private static string SafeStyleAttribute(string style)
    {
        var declarations = CssComment().Replace(style, " ")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsSafeCss);
        return string.Join("; ", declarations);
    }

    private static bool IsSafeValue(string value) => IsSafeCss(value);

    // True when the text, read as CSS, has nothing that runs script or loads another resource.
    // Checked with and without comments removed: the comment pattern does not know about quoted
    // strings, so a "/*" inside a string could otherwise hide a rule from the check.
    private static bool IsSafeCss(string css) => IsSafeCssText(css) && IsSafeCssText(CssComment().Replace(css, ""));

    private static bool IsSafeCssText(string css)
    {
        var compact = Compact(DecodeCssEscapes(css)).ToLowerInvariant();
        if (ForbiddenCss.Any(compact.Contains)) return false;
        foreach (Match url in CssUrl().Matches(compact))
        {
            if (!url.Groups["target"].Value.TrimStart('\'', '"').StartsWith('#')) return false;
        }
        return true;
    }

    // Removes whitespace and control characters, which browsers ignore inside a URL scheme.
    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c > ' ' && c != '\u007f' && !char.IsWhiteSpace(c) && !char.IsControl(c)) builder.Append(c);
        }
        return builder.ToString();
    }

    private static string DecodeCssEscapes(string css) =>
        CssEscape().Replace(css, m =>
        {
            var hex = m.Groups["hex"].Value;
            if (hex.Length == 0) return m.Groups["char"].Value;
            var code = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "�";
        });

    private static (int? Width, int? Height) ReadSize(XmlReader root)
    {
        var width = ParseLength(root.GetAttribute("width"));
        var height = ParseLength(root.GetAttribute("height"));
        if (width is not null && height is not null) return (width, height);

        var viewBox = root.GetAttribute("viewBox")?
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (viewBox is { Length: 4 }
            && TryParseNumber(viewBox[2], out var w) && TryParseNumber(viewBox[3], out var h))
        {
            return (ToSize(w), ToSize(h));
        }
        return (null, null);
    }

    private static int? ParseLength(string? value)
    {
        if (value is null) return null;
        var match = PixelLength().Match(value);
        return match.Success && TryParseNumber(match.Groups["number"].Value, out var number) ? ToSize(number) : null;
    }

    private static bool TryParseNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static int? ToSize(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded is >= 1 and <= int.MaxValue ? (int)rounded : null;
    }

    [GeneratedRegex(@"/\*.*?(\*/|$)", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"\\(?:(?<hex>[0-9a-fA-F]{1,6})[ \t\r\n\f]?|(?<char>[^\r\n\f0-9a-fA-F]))")]
    private static partial Regex CssEscape();

    [GeneratedRegex(@"url\((?<target>[^)]*)")]
    private static partial Regex CssUrl();

    [GeneratedRegex(@"^data:image/(png|jpeg|gif|webp)[;,]", RegexOptions.IgnoreCase)]
    private static partial Regex RasterDataUrl();

    [GeneratedRegex(@"^\s*(?<number>[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*(px)?\s*$")]
    private static partial Regex PixelLength();
}
