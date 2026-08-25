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

    // --- Log-scale companions ------------------------------------------------
    //
    // Same partials consume both. When the data has a heavy outlier squishing the
    // mass of the plot into a thin band at the bottom of the Y axis, the caller
    // flips to base-10 log so the sub-100ms cluster stays readable next to a
    // 3-second peak. Trigger: max(y) / max(min(y), 1) > 20. See
    // ChartAxisTicksLogScaleTests for the full contract.

    // Whether the (min, max) pair warrants log scale. Clamps min to 1 so a data
    // set that includes y=0 does not blow the ratio up to infinity — a 0..100
    // series has a real ratio of 100 (0 → 1), which is high enough to help.
    public static bool ShouldUseLogScale(int minValue, int maxValue)
    {
        if (maxValue <= 0) return false;
        if (maxValue < minValue) return false;
        var clampedMin = Math.Max(minValue, 1);
        return (double)maxValue / clampedMin > 20d;
    }

    // Log-scale Y tick fractions between the (clamped) min and max, mapped back
    // to 0..1 for the caller's plot. Powers of 10 when the range is >= 3 decades;
    // half-decade (1, 3, 10, 30, 100, 300, ...) otherwise so short-range plots
    // still get 5-ish visible ticks. Always includes fraction 0 (axis floor) and
    // fraction 1 (axis ceiling) so the axis ends are labelled the same way the
    // linear helper does.
    public static IReadOnlyList<double> YAxisFractionsLog(int minValue, int maxValue)
    {
        if (maxValue <= 0) return new[] { 0d, 1d };
        var clampedMin = Math.Max(minValue, 1);
        if (clampedMin >= maxValue) return new[] { 0d, 1d };

        var logMin = Math.Log10(clampedMin);
        var logMax = Math.Log10(maxValue);
        var span = logMax - logMin;
        if (span <= 0) return new[] { 0d, 1d };

        // Half-decade multipliers if the range is under 3 decades; otherwise
        // integer-decade only. 1 is always present (it's the log floor multiplier).
        var multipliers = span < 3d
            ? new[] { 1d, 3d }
            : new[] { 1d };

        var fractions = new SortedSet<double> { 0d, 1d };
        // Walk each decade in the range and emit fractional positions for every
        // (multiplier × 10^decade) that lands strictly between clampedMin and maxValue.
        var startDecade = (int)Math.Floor(logMin);
        var endDecade = (int)Math.Ceiling(logMax);
        for (var decade = startDecade; decade <= endDecade; decade++)
        {
            var decadeBase = Math.Pow(10, decade);
            foreach (var m in multipliers)
            {
                var value = decadeBase * m;
                if (value <= clampedMin || value >= maxValue) continue;
                var fraction = (Math.Log10(value) - logMin) / span;
                if (fraction > 0d && fraction < 1d)
                {
                    fractions.Add(fraction);
                }
            }
        }
        return fractions.ToArray();
    }

    // Formats a Y-axis label for the log scale. Fraction is 0..1 in log space
    // between (clampedMin, maxValue). Result is the underlying ms value in normal
    // "N0" grouping with the caller-supplied suffix ("10 ms" / "1,000 ms" / ...)
    // so the reader recognises the value even though the tick spacing is logarithmic.
    public static string YAxisLabelLog(double fraction, int minValue, int maxValue, string suffix = "")
    {
        if (maxValue <= 0) return "0" + suffix;
        var clampedMin = Math.Max(minValue, 1);
        var logMin = Math.Log10(clampedMin);
        var logMax = Math.Log10(Math.Max(maxValue, clampedMin + 1));
        var span = logMax - logMin;
        var value = span <= 0
            ? clampedMin
            : Math.Pow(10, logMin + fraction * span);
        return Math.Round(value).ToString("N0", CultureInfo.CurrentCulture) + suffix;
    }
}
