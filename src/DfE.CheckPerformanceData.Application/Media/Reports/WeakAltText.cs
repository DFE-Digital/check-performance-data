using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// Decides whether alt text describes its image. The rules run in this order, and the first that
/// matches is the problem: no text; a file name (an image extension, or the image's own file name
/// with or without its extension); a generic word on its own, such as "image" or "the logo"; fewer
/// than <see cref="MinimumLength"/> characters; the same words as the caption. Case, spaces at the
/// ends and full stops at the end do not count.
/// </summary>
public static partial class WeakAltText
{
    public const int MinimumLength = 5;

    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "image", "img", "photo", "photograph", "picture", "pic", "graphic", "logo", "screenshot",
        "screen shot", "icon", "illustration", "banner", "placeholder",
    };

    public static AltTextProblem Check(string? alt, string? fileName, string? caption)
    {
        if (string.IsNullOrWhiteSpace(alt)) return AltTextProblem.Missing;

        var text = Normalise(alt);
        if (ImageExtension().IsMatch(alt.Trim()) || IsOwnFileName(alt.Trim(), fileName)) return AltTextProblem.FileName;
        if (GenericWords.Contains(WithoutArticle(text))) return AltTextProblem.Generic;
        if (alt.Trim().Length < MinimumLength) return AltTextProblem.TooShort;
        if (!string.IsNullOrWhiteSpace(caption) && text == Normalise(caption)) return AltTextProblem.SameAsCaption;
        return AltTextProblem.None;
    }

    /// <summary>The words the reports show for a problem.</summary>
    public static string Describe(AltTextProblem problem) => problem switch
    {
        AltTextProblem.Missing => "No alt text",
        AltTextProblem.Generic => "Only a general word, such as ‘image’ or ‘photo’",
        AltTextProblem.FileName => "A file name",
        AltTextProblem.TooShort => $"Shorter than {MinimumLength} characters",
        AltTextProblem.SameAsCaption => "The same as the caption",
        _ => "",
    };

    private static bool IsOwnFileName(string alt, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = fileName.Trim();
        return string.Equals(alt, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(alt, Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
    }

    // Lower case, single spaces, with no full stops or other end punctuation.
    private static string Normalise(string text) =>
        Spaces().Replace(text.Trim().TrimEnd('.', '!', '?', ',', ';', ':').Trim(), " ").ToLowerInvariant();

    private static string WithoutArticle(string text)
    {
        foreach (var article in new[] { "a ", "an ", "the " })
        {
            if (text.StartsWith(article, StringComparison.Ordinal)) return text[article.Length..];
        }
        return text;
    }

    [GeneratedRegex(@"\.(png|jpe?g|gif|webp|svg|bmp|tiff?|heic|avif)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageExtension();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
