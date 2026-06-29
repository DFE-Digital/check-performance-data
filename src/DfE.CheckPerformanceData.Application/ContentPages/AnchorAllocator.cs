using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Allocates unique anchors for a page's heading widgets. The base anchor is the heading text run
// through WikiSlug (the single slugify rule); within one page, repeated or empty headings get a
// numeric suffix so every <section id> / nav target is unique. Stateful: one instance per page.
public sealed class AnchorAllocator
{
    private const string Fallback = "section";
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public string Allocate(string? headingText)
    {
        var baseSlug = WikiSlug.Generate(headingText ?? string.Empty);
        if (baseSlug.Length == 0) baseSlug = Fallback;

        var candidate = baseSlug;
        var n = 1;
        while (!_used.Add(candidate))
        {
            n++;
            candidate = $"{baseSlug}-{n}";
        }

        return candidate;
    }
}
