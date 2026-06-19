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
    public static string? ResolveActiveKey(string? requestPath, IEnumerable<IAdminNavEntry> entries)
    {
        var path = Normalise(requestPath);
        if (path.Length == 0)
            return null;

        string? bestKey = null;
        var bestLength = -1;

        foreach (var entry in entries)
        {
            var url = Normalise(entry.Url);
            // Skip containers / placeholders (empty or "#"): they are not navigable pages.
            if (url.Length == 0 || url == "#")
                continue;

            // Match the path exactly, or as a descendant of this entry's URL. Comparing against
            // url + "/" prevents /admin/queues from spuriously matching /admin/queues-other.
            var isMatch = string.Equals(path, url, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(url + "/", StringComparison.OrdinalIgnoreCase);
            if (!isMatch)
                continue;

            if (url.Length > bestLength)
            {
                bestLength = url.Length;
                bestKey = entry.Key;
            }
        }

        return bestKey;
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
