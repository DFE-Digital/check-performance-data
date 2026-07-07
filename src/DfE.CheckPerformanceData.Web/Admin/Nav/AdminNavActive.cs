namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Resolves which admin nav entry the current request path belongs to, so the left-hand tree can
// highlight it (aria-current=page) and force-expand its ancestor groups. The recursive node
// partial already does the highlight/expand once it is handed an ActiveKey; this is the missing
// piece that derives that key from the live path.
//
// Matching is longest-Url-prefix: an entry matches when the path equals its Url or sits beneath it
// (Url + "/..."), and the entry with the longest matching Url wins. That makes a deep descendant
// path (e.g. /admin/observability/journey/REF-1, which is not itself a nav entry) light its
// nearest ancestor entry (the Pipeline dashboard) rather than nothing, and a more specific child
// (/admin/observability/transactions) beat its parent. Container entries with no real Url (the
// group/sub-group headings) are never matched as the active leaf.
public static class AdminNavActive
{
    // Overload used by _AdminLayout: request path and query string separately, so entries that
    // carry a query (e.g. /admin/content-blocks?page=/foo in the content-blocks tree) light up
    // only for the exact matching query and beat their path-only parent on tiebreak.
    public static string? ResolveActiveKey(string? requestPath, IEnumerable<IAdminNavEntry> entries)
        => ResolveActiveKey(requestPath, requestQuery: null, entries);

    public static string? ResolveActiveKey(string? requestPath, string? requestQuery, IEnumerable<IAdminNavEntry> entries)
    {
        var path = Normalise(requestPath);
        if (path.Length == 0)
            return null;

        var reqQueryPairs = ParseQuery(requestQuery);

        string? bestKey = null;
        var bestLength = -1;
        var bestHasQuery = false;

        foreach (var entry in entries)
        {
            var (urlPath, urlQuery) = SplitUrl(entry.Url);
            // Skip containers / placeholders (empty or "#"): they are not navigable pages.
            if (urlPath.Length == 0 || urlPath == "#")
                continue;

            // Path must match exactly or the request must sit beneath the entry.
            var pathMatches = string.Equals(path, urlPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(urlPath + "/", StringComparison.OrdinalIgnoreCase);
            if (!pathMatches)
                continue;

            // If the entry carries a query, the request must include every entry query pair with
            // the matching value. Otherwise the entry's ambition is too specific and it should not
            // light up.
            var entryHasQuery = urlQuery.Length > 0;
            if (entryHasQuery && !QueryContainsAll(reqQueryPairs, urlQuery))
                continue;

            // Entries with a query beat entries without on tiebreak (they're more specific).
            // Otherwise the longest matching URL path wins.
            var better = (entryHasQuery && !bestHasQuery)
                || (entryHasQuery == bestHasQuery && urlPath.Length > bestLength);
            if (better)
            {
                bestLength = urlPath.Length;
                bestHasQuery = entryHasQuery;
                bestKey = entry.Key;
            }
        }

        return bestKey;
    }

    private static (string Path, string Query) SplitUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (string.Empty, string.Empty);
        var i = url.IndexOf('?');
        if (i < 0) return (Normalise(url), string.Empty);
        return (Normalise(url[..i]), url[(i + 1)..]);
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return pairs;
        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var kv in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = kv.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? kv : kv[..eq]);
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(kv[(eq + 1)..]);
            pairs[key] = value;
        }
        return pairs;
    }

    private static bool QueryContainsAll(Dictionary<string, string> request, string entryQuery)
    {
        var entryPairs = ParseQuery(entryQuery);
        foreach (var (key, value) in entryPairs)
        {
            if (!request.TryGetValue(key, out var actual) ||
                !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // Lower-cases nothing (the comparison is ordinal-ignore-case), but strips a query string and a
    // single trailing slash so "/x/", "/x?y=1" and "/x" all resolve alike. A null/blank path
    // collapses to empty.
    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var path = value;
        var query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];

        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        return path;
    }
}
