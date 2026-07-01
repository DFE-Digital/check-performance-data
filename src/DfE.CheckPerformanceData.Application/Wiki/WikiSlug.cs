using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Wiki;

// Single source of truth for turning a page title into its URL slug. Used by WikiService when
// creating pages, and by content staging to build a virtual slug path from a chain of titles.
public static partial class WikiSlug
{
    public static string Generate(string title)
    {
        var slug = title.ToLowerInvariant();
        slug = InvalidChars().Replace(slug, "");
        slug = Whitespace().Replace(slug, "-");
        slug = MultipleDashes().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex InvalidChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleDashes();
}
