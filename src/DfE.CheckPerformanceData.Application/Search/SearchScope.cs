namespace DfE.CheckPerformanceData.Application.Search;

// A search scope names the pages a search is limited to: one page path, or several separated by
// commas ("guidance/ks4,guidance/results-enquiries"). Each path means that page and everything
// beneath it. Page paths are lowercase letters, digits, hyphens and slashes, so a comma can never
// be part of one and the separator is unambiguous. A lone path reads exactly as it always has,
// which is what keeps every existing widget and /search?scope= link working.
public static class SearchScope
{
    private const char Separator = ',';

    // The distinct page paths in a scope, in the order given, with surrounding slashes and blanks
    // removed. Empty when the scope is missing or names nothing.
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var paths = new List<string>();
        foreach (var part in raw.Split(Separator))
        {
            var path = part.Trim().Trim('/');
            if (path.Length > 0 && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }
        return paths;
    }

    // The canonical single-string form, or null when the scope names no page (search everything).
    public static string? Normalise(string? raw)
    {
        var paths = Parse(raw);
        return paths.Count == 0 ? null : string.Join(Separator, paths);
    }

    // True when the URL is one of the scoped pages or sits beneath one. An empty scope covers
    // everything. The comparison respects segment boundaries, so /guidance/ks4-archive is not
    // inside guidance/ks4.
    public static bool Covers(IReadOnlyList<string> scope, string url)
    {
        if (scope.Count == 0) return true;

        var path = url.Trim().Trim('/');
        return scope.Any(s => path.Equals(s, StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase));
    }
}
