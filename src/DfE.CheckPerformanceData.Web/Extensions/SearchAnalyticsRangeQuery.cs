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
//   range == "custom"  →  "range=custom&from=<iso>&to=<yyyy-MM-dd>"
//   otherwise          →  "range=<key>"
//
// The two bounds are deliberately rendered differently because they mean different
// things to ResolveWindow. `from` is an inclusive instant, so it round-trips as full
// ISO-8601. `to` is an inclusive end DATE which ResolveWindow widens to the following
// midnight to form the exclusive query bound — so the link has to hand back the end
// date, not the widened bound, or every drill-in click would walk the window forward
// another day.
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
        var to = Uri.EscapeDataString(
            InclusiveEndDate(toUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return $"range=custom&from={from}&to={to}";
    }

    // Converts the resolved exclusive upper bound back into the inclusive end date the
    // admin actually chose — the value the date input and the drill-in links must show.
    // One implementation so the off-by-one lives in a single place.
    public static DateTime InclusiveEndDate(DateTime exclusiveToUtc) =>
        exclusiveToUtc.AddTicks(-1).Date;
}
