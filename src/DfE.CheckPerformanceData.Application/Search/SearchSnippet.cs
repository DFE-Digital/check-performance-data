using System.Net;

namespace DfE.CheckPerformanceData.Application.Search;

// Shared windowed-snippet builder used by SiteSearchService and ContentBlockSearchService
// to build every search-result snippet. Extracted from the two BuildSnippet methods that
// previously duplicated identical logic — both callers now delegate here.
//
// Produces safe snippet HTML: everything in the surrounding window is HTML-encoded via
// WebUtility.HtmlEncode, and only the first case-insensitive match of `term` is wrapped
// in <mark>...</mark>. The <mark> is the only markup that reaches the results page —
// the two-sided-sink invariant (encoded form present AND raw form absent) is pinned by
// SearchSnippetTests. If the term is not found the first 200 chars are encoded and
// returned with a trailing "…" if the source was longer.
public static class SearchSnippet
{
    /// <summary>
    /// Build a ~200-char window around the first case-insensitive match of <paramref name="term"/>
    /// in <paramref name="source"/>, HTML-encoding the surrounding text and wrapping the matched
    /// span in <c>&lt;mark&gt;...&lt;/mark&gt;</c>. Returns an empty string for null or empty source.
    /// </summary>
    public static string BuildWindow(string? source, string term)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var idx = source.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            var head = source.Length <= 200 ? source : source[..200] + "…";
            return WebUtility.HtmlEncode(head);
        }

        var start = Math.Max(0, idx - 60);
        var length = Math.Min(source.Length - start, 200);
        var window = source.Substring(start, length);
        var wi = window.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        var before = WebUtility.HtmlEncode(window[..wi]);
        var match = WebUtility.HtmlEncode(window.Substring(wi, term.Length));
        var after = WebUtility.HtmlEncode(window[(wi + term.Length)..]);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = start + length < source.Length ? "…" : string.Empty;
        return $"{prefix}{before}<mark>{match}</mark>{after}{suffix}";
    }
}
