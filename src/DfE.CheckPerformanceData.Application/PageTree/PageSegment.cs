using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// The formatting rule for a page's URL segment, the part of its path between two slashes:
/// lowercase letters and digits in groups joined by single hyphens, for example
/// <c>spring-checking-exercise</c>. Editing, creating and importing a page all apply this
/// one rule, so a segment the CMS saves is always one an import will take.
/// </summary>
public static partial class PageSegment
{
    /// <summary>
    /// Trims and lowercases a segment. Only case and surrounding spaces are repaired; anything
    /// else wrong with it is left for <see cref="IsValid"/> to reject.
    /// </summary>
    public static string Normalise(string? segment) => (segment ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? segment) => segment is not null && Pattern().IsMatch(segment);

    // \z, not $: $ also matches before a final newline.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
