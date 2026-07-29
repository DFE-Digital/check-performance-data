using System.Globalization;

namespace DfE.CheckPerformanceData.Application.Analytics;

// Adaptive tick generator shared by every time-series chart partial on the search-analytics
// dashboard. One code path decides how many X-axis ticks to place + how each label reads
// based on the window width, and how many Y-axis ticks to place based on the max value.
// Keeps every SVG partial visually consistent regardless of window preset (24h / 7d / 30d
// / 90d / custom).
public static class ChartAxisTicks
{
    // Picks the subset of bucket indices to label on the X-axis. Returns a list of (index,
    // label) pairs — index is a position into the caller's buckets array, label is what
    // the SVG <text> element renders. Format flexes with the window width the buckets span:
    //  - <= 24 hours    : HH:mm                      (e.g. "00:00", "04:00", "08:00", ...)
    //  - <= 7 days      : "ddd d"    (weekday+day)   (e.g. "Wed 24")
    //  - <= 30 days     : "d MMM"    (day+month)     (e.g. "24 Jul")
    //  - <= 90 days     : "d MMM" every Monday       (weekly majors)
    //  -  > 90 days     : "MMM yyyy"                 (monthly)
    // Never emits more labels than can plausibly fit in a ~600px-wide plot area — every
    // label is treated as ~72 px wide (8 chars × 8 px + slack) and the count is dropped to
    // the ceiling that avoids overlap.
    public static IReadOnlyList<(int Index, string Label)> XAxisTicks(
        IReadOnlyList<DateTime> bucketStarts,
        double plotWidthPx = 620d)
    {
        if (bucketStarts is null || bucketStarts.Count == 0)
            return Array.Empty<(int, string)>();

        if (bucketStarts.Count == 1)
            return new[] { (0, FormatSingleton(bucketStarts[0])) };

        var span = bucketStarts[^1] - bucketStarts[0];
        var format = ChooseFormat(span);

        // Character budget per label: rough heuristic — 8px per character + 24 px slack.
        // Cap the tick count so labels do not overlap on the plot area.
        var sampleLabel = bucketStarts[0].ToString(format, CultureInfo.InvariantCulture);
        var labelWidthPx = Math.Max(48d, sampleLabel.Length * 8d + 24d);
        var maxLabels = (int)Math.Floor(plotWidthPx / labelWidthPx);
        if (maxLabels < 2) maxLabels = 2;

        var targetCount = ChooseTargetTickCount(span, maxLabels);
        if (targetCount > bucketStarts.Count) targetCount = bucketStarts.Count;

        var indices = new List<int>(targetCount);
        // Evenly spaced across the range, always including endpoints.
        for (var t = 0; t < targetCount; t++)
        {
            var raw = (long)Math.Round((double)t * (bucketStarts.Count - 1) / (targetCount - 1));
            var idx = (int)raw;
            if (indices.Count == 0 || indices[^1] != idx) indices.Add(idx);
        }
        if (indices[^1] != bucketStarts.Count - 1) indices.Add(bucketStarts.Count - 1);

        var ticks = new List<(int Index, string Label)>(indices.Count);
        foreach (var i in indices)
        {
            ticks.Add((i, bucketStarts[i].ToString(format, CultureInfo.InvariantCulture)));
        }
        return ticks;
    }

    // Picks 3-5 Y-axis tick fractions (0..1) whose values would land on "nice" numbers
    // (1, 2, 5, 10 × 10^n). Always includes 0 as the baseline. Returns the fractional
    // positions so the caller can format the label (with its own suffix like "ms" or "%").
    public static IReadOnlyList<double> YAxisFractions(double maxValue)
    {
        if (double.IsNaN(maxValue) || maxValue <= 0) return new[] { 0d, 1d };

        var target = 4; // aim for 4 major ticks including 0
        var raw = maxValue / target;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / mag;
        var step = norm < 1.5 ? 1 : (norm < 3 ? 2 : (norm < 7 ? 5 : 10));
        var niceStep = step * mag;
        var ticks = new List<double>();
        for (double v = 0; v <= maxValue + niceStep * 0.001; v += niceStep)
        {
            ticks.Add(v / maxValue);
            if (ticks.Count > 6) break;
        }
        if (ticks[^1] < 0.999) ticks.Add(1d);
        return ticks;
    }

    // Formats a Y-axis label given a fractional position and the max value the fraction
    // is measured against. Uses "N0" grouping (14,190 not 14190).
    public static string YAxisLabel(double fraction, double maxValue, string suffix = "")
    {
        var raw = fraction * maxValue;
        return raw.ToString("N0", CultureInfo.CurrentCulture) + suffix;
    }

    // The format the X-axis labels use for the chart's tooltip / hover crosshair. Same
    // rule as XAxisTicks — flex by window width — but always includes an "HH:mm" component
    // so the hover reveals precise time even for a wide-window chart.
    public static string TooltipFormat(TimeSpan windowSpan)
    {
        if (windowSpan <= TimeSpan.FromHours(24)) return "ddd d MMM, HH:mm";
        if (windowSpan <= TimeSpan.FromDays(7))   return "ddd d MMM, HH:mm";
        if (windowSpan <= TimeSpan.FromDays(30))  return "ddd d MMM, HH:mm";
        if (windowSpan <= TimeSpan.FromDays(90))  return "ddd d MMM yyyy";
        return "d MMM yyyy";
    }

    // Picks how many ticks to place on the X-axis based on the window's total span. Also
    // clamped by the max labels the caller reports fit.
    private static int ChooseTargetTickCount(TimeSpan span, int maxLabels)
    {
        int desired;
        if (span <= TimeSpan.FromHours(24))     desired = 7;
        else if (span <= TimeSpan.FromDays(7))  desired = 7;
        else if (span <= TimeSpan.FromDays(30)) desired = 7;
        else if (span <= TimeSpan.FromDays(90)) desired = 7;
        else                                    desired = 6;
        return Math.Min(desired, maxLabels);
    }

    private static string ChooseFormat(TimeSpan span)
    {
        if (span <= TimeSpan.FromHours(24))     return "HH:mm";
        if (span <= TimeSpan.FromDays(7))       return "ddd d";
        if (span <= TimeSpan.FromDays(30))      return "d MMM";
        if (span <= TimeSpan.FromDays(90))      return "d MMM";
        return "MMM yyyy";
    }

    private static string FormatSingleton(DateTime ts) =>
        ts.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
}
