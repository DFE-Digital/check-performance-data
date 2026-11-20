using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Reads and writes the resize and crop settings of a media URL:
/// <c>w</c> (one of <see cref="AllowedWidths"/>), <c>crop=x,y,w,h</c>, <c>ar=16:9</c>,
/// <c>focal=x,y</c> (only with <c>ar</c>) and <c>sig</c>. Fractions are 0 to 1, rounded to
/// 3 decimal places. A setting given twice, or in any other shape, makes the whole query invalid.
/// Other names are ignored, because they cannot change the image.
///
/// <see cref="Canonical"/> writes one fixed form for each rendition. The signature and the
/// stored copy's name are both made from it, so URLs that differ only in how a number is written
/// share one signature and one stored copy.
/// </summary>
public static class MediaRenditionQuery
{
    /// <summary>The only widths a copy can be made at, so a URL cannot ask for any other size.</summary>
    public static readonly IReadOnlyList<int> AllowedWidths = [320, 640, 960, 1280, 1920];

    public const int MaxAspectTerm = 1000;
    private const int Places = 3;

    private const string WidthKey = "w";
    private const string CropKey = "crop";
    private const string AspectKey = "ar";
    private const string FocalKey = "focal";
    private const string SignatureKey = "sig";

    private static readonly string[] Keys = [WidthKey, CropKey, AspectKey, FocalKey, SignatureKey];

    public static MediaRenditionQueryResult Parse(IEnumerable<KeyValuePair<string, string?>> query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in query)
        {
            if (!Keys.Contains(key, StringComparer.Ordinal)) continue;
            if (!values.TryAdd(key, value ?? "")) return MediaRenditionQueryResult.Invalid;
        }
        values.TryGetValue(SignatureKey, out var signature);

        int? width = null;
        MediaCrop? crop = null;
        MediaAspectRatio? aspect = null;
        MediaFocalPoint? focal = null;
        if (values.TryGetValue(WidthKey, out var w))
        {
            if (!TryParseWidth(w, out var parsed)) return MediaRenditionQueryResult.Invalid;
            width = parsed;
        }
        if (values.TryGetValue(CropKey, out var c))
        {
            crop = ParseCrop(c);
            if (crop is null) return MediaRenditionQueryResult.Invalid;
        }
        if (values.TryGetValue(AspectKey, out var a))
        {
            aspect = ParseAspect(a);
            if (aspect is null) return MediaRenditionQueryResult.Invalid;
        }
        if (values.TryGetValue(FocalKey, out var f))
        {
            focal = ParseFocal(f);
            if (focal is null || aspect is null) return MediaRenditionQueryResult.Invalid;
        }

        var rendition = width is null && crop is null && aspect is null
            ? null
            : new MediaRendition(width, crop, aspect, focal);
        return new MediaRenditionQueryResult(true, rendition, signature);
    }

    /// <summary>The fixed text form of a rendition, without a signature, for example <c>w=640&amp;ar=16:9</c>.</summary>
    public static string Canonical(MediaRendition rendition)
    {
        var parts = new List<string>(4);
        if (rendition.Width is { } width) parts.Add($"{WidthKey}={width.ToString(CultureInfo.InvariantCulture)}");
        if (rendition.Crop is { } c) parts.Add($"{CropKey}={Number(c.X)},{Number(c.Y)},{Number(c.Width)},{Number(c.Height)}");
        if (rendition.Aspect is { } a) parts.Add($"{AspectKey}={a.Width.ToString(CultureInfo.InvariantCulture)}:{a.Height.ToString(CultureInfo.InvariantCulture)}");
        if (rendition.Focal is { } f) parts.Add($"{FocalKey}={Number(f.X)},{Number(f.Y)}");
        return string.Join('&', parts);
    }

    /// <summary>The query string to put on a URL: the canonical settings and then the signature.</summary>
    public static string WithSignature(MediaRendition rendition, string signature) =>
        $"{Canonical(rendition)}&{SignatureKey}={signature}";

    /// <summary>Letters and digits naming the stored copy of a rendition (see <see cref="MediaBlobKeys.Rendition"/>).</summary>
    public static string Hash(MediaRendition rendition) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(rendition))))[..32];

    /// <summary>Rounds a rendition the same way <see cref="Parse"/> does, and refuses one it would refuse.</summary>
    public static MediaRendition Normalise(int? width, MediaCrop? crop, MediaAspectRatio? aspect, MediaFocalPoint? focal)
    {
        if (width is { } w && !AllowedWidths.Contains(w))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be one of the fixed widths.");
        if (focal is not null && aspect is null)
            throw new ArgumentException("A focal point needs an aspect ratio.", nameof(focal));

        var roundedCrop = crop is null ? null : CheckCrop(Round(crop.X), Round(crop.Y), Round(crop.Width), Round(crop.Height))
            ?? throw new ArgumentException("The crop must lie inside the image.", nameof(crop));
        if (aspect is not null && (!IsAspectTerm(aspect.Width) || !IsAspectTerm(aspect.Height)))
            throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Each side of the aspect ratio must be 1 to 1000.");
        var roundedFocal = focal is null ? null : CheckFocal(Round(focal.X), Round(focal.Y))
            ?? throw new ArgumentException("The focal point must lie inside the image.", nameof(focal));

        return new MediaRendition(width, roundedCrop, aspect, roundedFocal);
    }

    private static bool TryParseWidth(string text, out int width)
    {
        width = 0;
        // Digits only, written the one way the server writes them.
        if (text.Length == 0 || text[0] == '0' || !text.All(char.IsAsciiDigit)) return false;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out width) && AllowedWidths.Contains(width);
    }

    private static MediaCrop? ParseCrop(string text)
    {
        var numbers = ParseFractions(text, 4);
        return numbers is null ? null : CheckCrop(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static MediaFocalPoint? ParseFocal(string text)
    {
        var numbers = ParseFractions(text, 2);
        return numbers is null ? null : CheckFocal(numbers[0], numbers[1]);
    }

    private static MediaAspectRatio? ParseAspect(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || !IsAspectTerm(width) || !IsAspectTerm(height))
            return null;
        return new MediaAspectRatio(width, height);
    }

    private static bool IsAspectTerm(int value) => value is >= 1 and <= MaxAspectTerm;

    // Plain decimals only (no signs, exponents or NaN), each rounded to 3 places.
    private static decimal[]? ParseFractions(string text, int count)
    {
        var parts = text.Split(',');
        if (parts.Length != count) return null;
        var numbers = new decimal[count];
        for (var i = 0; i < count; i++)
        {
            if (parts[i].Length is 0 or > 16
                || !decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                return null;
            numbers[i] = Round(number);
        }
        return numbers;
    }

    private static MediaCrop? CheckCrop(decimal x, decimal y, decimal width, decimal height) =>
        x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= 1 && y + height <= 1
            ? new MediaCrop(x, y, width, height)
            : null;

    private static MediaFocalPoint? CheckFocal(decimal x, decimal y) =>
        x is >= 0 and <= 1 && y is >= 0 and <= 1 ? new MediaFocalPoint(x, y) : null;

    private static decimal Round(decimal value) => Math.Round(value, Places, MidpointRounding.AwayFromZero);

    // The shortest form: 0.5, not 0.500; 1, not 1.0.
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
