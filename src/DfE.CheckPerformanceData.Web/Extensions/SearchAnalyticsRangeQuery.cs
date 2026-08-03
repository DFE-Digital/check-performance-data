using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Builds the range querystring for search-analytics drill-in links.
//
// ResolveWindow on the controller requires BOTH `from` and `to` when range==custom
// and otherwise falls back to the default 7-day window. Every drill-in link that
// only threads `?range=<key>` (without from/to) would therefore land on the last
// 7 days on a "custom" click while displaying "custom" in the range label —
// silently different data than the surface the click came from.
//
// This helper renders:
//   range == "custom"  →  "range=custom&from=<iso>&to=<iso>"
//   otherwise          →  "range=<key>"
//
// ISO-8601 UTC format, lossless round-trip: same string ResolveWindow reads back.
public static class SearchAnalyticsRangeQuery
{
    public static string Build(string rangeKey, DateTime fromUtc, DateTime toUtc)
    {
        var key = string.IsNullOrEmpty(rangeKey) ? "7d" : rangeKey;
        if (!string.Equals(key, "custom", StringComparison.Ordinal))
        {
            return "range=" + Uri.EscapeDataString(key);
        }
        var from = Uri.EscapeDataString(fromUtc.ToString("o", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(toUtc.ToString("o", CultureInfo.InvariantCulture));
        return $"range=custom&from={from}&to={to}";
    }
}
