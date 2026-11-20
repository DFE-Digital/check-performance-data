using System.Globalization;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// The Image widget's props and the values each one may hold. Every prop is a string, like the
/// props of the other widgets, because the editor posts them as form fields. The save check
/// (<see cref="ImageWidgetValidator"/>) and the view (<see cref="ImageWidgetViewBuilder"/>) read
/// them through these helpers, so both apply the same rules.
/// </summary>
public static class ImageWidgetProps
{
    public const string Type = "image";

    public const string MediaId = "mediaId";
    public const string Alt = "alt";
    public const string Decorative = "decorative";
    public const string Caption = "caption";
    public const string Credit = "credit";
    public const string Width = "width";
    public const string Align = "align";
    public const string Href = "href";
    public const string Crop = "crop";
    public const string Aspect = "aspect";
    public const string Focal = "focal";

    public const string FullWidth = "full";

    /// <summary>The GOV.UK grid widths an image can take, widest first.</summary>
    public static readonly IReadOnlyList<string> Widths = [FullWidth, "two-thirds", "one-half", "one-third"];

    /// <summary>Where an image narrower than its column sits. Empty means the normal flow.</summary>
    public static readonly IReadOnlyList<string> Aligns = ["", "left", "right", "centre"];

    /// <summary>The aspect ratios an editor can choose. Empty keeps the image's own shape.</summary>
    public static readonly IReadOnlyList<string> Aspects = ["", "16:9", "4:3", "1:1", "3:2"];

    /// <summary>A prop's text, or empty when it is missing or is not a string.</summary>
    public static string Text(JsonObject? props, string key)
    {
        if (props is null || !props.TryGetPropertyValue(key, out var node) || node is not JsonValue value) return "";
        return value.TryGetValue<string>(out var text) ? text : "";
    }

    public static bool IsDecorative(JsonObject? props) =>
        string.Equals(Text(props, Decorative), "true", StringComparison.OrdinalIgnoreCase);

    public static bool TryMediaId(JsonObject? props, out Guid id) =>
        Guid.TryParse(Text(props, MediaId).Trim(), out id) && id != Guid.Empty;

    /// <summary>Reads "x,y,width,height" as fractions of the image. Null when empty or invalid.</summary>
    public static MediaCrop? ParseCrop(string text)
    {
        var numbers = Fractions(text, 4);
        if (numbers is null) return null;
        var crop = new MediaCrop(numbers[0], numbers[1], numbers[2], numbers[3]);
        return Fits(() => MediaRenditionQuery.Normalise(null, crop, null, null)) ? crop : null;
    }

    /// <summary>Reads "x,y" as fractions of the image. Null when empty or invalid.</summary>
    public static MediaFocalPoint? ParseFocal(string text)
    {
        var numbers = Fractions(text, 2);
        return numbers is not null && numbers.All(n => n is >= 0 and <= 1) ? new MediaFocalPoint(numbers[0], numbers[1]) : null;
    }

    /// <summary>Reads one of <see cref="Aspects"/>. Null when empty or not in the list.</summary>
    public static MediaAspectRatio? ParseAspect(string text)
    {
        if (text.Length == 0 || !Aspects.Contains(text, StringComparer.Ordinal)) return null;
        var parts = text.Split(':');
        return new MediaAspectRatio(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static decimal[]? Fractions(string text, int count)
    {
        var parts = text.Split(',');
        if (parts.Length != count) return null;
        var numbers = new decimal[count];
        for (var i = 0; i < count; i++)
        {
            var part = parts[i].Trim();
            if (part.Length is 0 or > 16
                || !decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }
        return numbers;
    }

    private static bool Fits(Action check)
    {
        try
        {
            check();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
