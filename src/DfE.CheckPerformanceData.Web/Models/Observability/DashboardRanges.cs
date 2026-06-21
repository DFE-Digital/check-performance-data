using DfE.CheckPerformanceData.Application.Observability;
using static DfE.CheckPerformanceData.Application.Observability.ThroughputGranularity;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// One selectable dashboard window: the query-string value, its display label, the bucket sizes that
// make sense for it, and a function computing its [From, To) window from "now". The granularity
// pairings keep a chart legible — a one-hour window in day buckets is a single meaningless bar, and
// a seven-day window in minute buckets is ten thousand unreadable points. Named calendar ranges
// (yesterday, last week, last month, ...) end BEFORE now; the "this period" and rolling ranges end
// at now.
public sealed record DashboardRangeOption(
    string Value,
    string Label,
    ThroughputGranularity DefaultGranularity,
    IReadOnlyList<ThroughputGranularity> AllowedGranularities,
    Func<DateTime, (DateTime From, DateTime To)> WindowFor)
{
    public (DateTime From, DateTime To) Window(DateTime nowUtc) => WindowFor(nowUtc);

    // True for the "today" range only: its window is since-midnight-to-now, so its chart series ARE
    // the headline-tile series and the controller can reuse them rather than re-querying.
    public bool IsToday { get; init; }
}

// The resolved window for a request: the option, the concrete [From, To) (already computed for
// custom from the query params and clamped), and the granularity pairing to validate against (the
// option's own, or — for a custom range — the pairing derived from the span).
public sealed record ResolvedRange(
    DashboardRangeOption Option,
    DateTime From,
    DateTime To,
    ThroughputGranularity DefaultGranularity,
    IReadOnlyList<ThroughputGranularity> AllowedGranularities);

// The server-side allow-list behind the dashboard's range/granularity form. Both selections resolve
// against it before any query runs, so a hand-edited query string can never reach the aggregation
// SQL: an unknown range or an unpaired granularity snaps to a safe default rather than erroring —
// the form is a GET and must always render a dashboard.
public static class DashboardRanges
{
    public const string DefaultValue = "today";
    public const string CustomValue = "custom";

    // The widest a custom window may span — matches the query-layer backstop so a custom range is
    // clamped here and never trips the persistence guard. The span-derived granularity pairing is
    // what actually bounds the bucket count.
    public static readonly TimeSpan MaxCustomSpan = TimeSpan.FromDays(1830);

    // Every granularity a user can pick somewhere, coarsest-legible last — the custom option offers
    // all of them and ResolveWindow narrows to the span-appropriate subset. Declared before All so it
    // is initialised when the custom option below references it (static fields init in textual order).
    public static readonly IReadOnlyList<ThroughputGranularity> AllSelectableGranularities = new[]
    {
        Minute, FiveMinute, TenMinute, Hour, Day, Week, Month, Quarter, Year,
    };

    public static readonly IReadOnlyList<DashboardRangeOption> All = new[]
    {
        // --- Calendar ranges (today first; "this period" ends at now, the others end before now) ---
        new DashboardRangeOption("today", "Today", Hour,
            new[] { TenMinute, Hour },
            now => (now.Date, now)) { IsToday = true },
        new DashboardRangeOption("yesterday", "Yesterday", Hour,
            new[] { TenMinute, Hour },
            now => (now.Date.AddDays(-1), now.Date)),
        new DashboardRangeOption("thisweek", "This week", Day,
            new[] { Hour, Day },
            now => (StartOfWeek(now), now)),
        new DashboardRangeOption("lastweek", "Last week", Day,
            new[] { Hour, Day },
            now => (StartOfWeek(now).AddDays(-7), StartOfWeek(now))),
        new DashboardRangeOption("thismonth", "This month", Day,
            new[] { Day, Week },
            now => (StartOfMonth(now), now)),
        new DashboardRangeOption("lastmonth", "Last month", Day,
            new[] { Day, Week },
            now => (StartOfMonth(now).AddMonths(-1), StartOfMonth(now))),
        new DashboardRangeOption("thisquarter", "This quarter", Week,
            new[] { Day, Week, Month },
            now => (StartOfQuarter(now), now)),
        new DashboardRangeOption("lastquarter", "Last quarter", Week,
            new[] { Day, Week, Month },
            now => (StartOfQuarter(now).AddMonths(-3), StartOfQuarter(now))),
        new DashboardRangeOption("thisyear", "This year", Month,
            new[] { Week, Month, Quarter },
            now => (StartOfYear(now), now)),
        new DashboardRangeOption("lastyear", "Last year", Month,
            new[] { Week, Month, Quarter },
            now => (StartOfYear(now).AddYears(-1), StartOfYear(now))),

        // --- Rolling windows, kept for fine-grained recent inspection ---
        new DashboardRangeOption("1h", "Last hour", Minute,
            new[] { Minute, FiveMinute },
            now => (now.AddHours(-1), now)),
        new DashboardRangeOption("6h", "Last 6 hours", FiveMinute,
            new[] { FiveMinute, TenMinute, Hour },
            now => (now.AddHours(-6), now)),
        new DashboardRangeOption("24h", "Last 24 hours", Hour,
            new[] { TenMinute, Hour },
            now => (now.AddHours(-24), now)),
        new DashboardRangeOption("7d", "Last 7 days", Day,
            new[] { Hour, Day },
            now => (now.AddDays(-7), now)),

        // --- Custom from/to: the concrete window and granularity pairing are computed from the
        //     query params via ResolveWindow/ForSpan; this entry just renders the option and gives a
        //     fallback window when no dates are supplied. ---
        new DashboardRangeOption("custom", "Custom range", Day,
            AllSelectableGranularities,
            now => (now.AddDays(-7), now)),
    };

    public static DashboardRangeOption Resolve(string? range) =>
        All.FirstOrDefault(o => string.Equals(o.Value, range, StringComparison.OrdinalIgnoreCase))
        ?? All.Single(o => o.Value == DefaultValue);

    // Resolves the full window for a request: the named option's calendar window, or — for a custom
    // range — the from/to query params (to defaults to now), guarded against an inverted/empty span
    // and clamped to MaxCustomSpan, with the granularity pairing derived from the span.
    public static ResolvedRange ResolveWindow(string? range, DateTime? fromUtc, DateTime? toUtc, DateTime nowUtc)
    {
        var option = Resolve(range);

        if (option.Value == CustomValue)
        {
            var to = toUtc ?? nowUtc;
            var from = fromUtc ?? to.AddDays(-7);
            if (to <= from) from = to.AddDays(-7);          // inverted/empty → a sane default span
            if (to - from > MaxCustomSpan) from = to - MaxCustomSpan; // clamp to the backstop
            var (def, allowed) = ForSpan(to - from);
            return new ResolvedRange(option, from, to, def, allowed);
        }

        var (f, t) = option.Window(nowUtc);
        if (t <= f) f = t.AddMinutes(-1); // exact-midnight "today" / clock-skew guard
        return new ResolvedRange(option, f, t, option.DefaultGranularity, option.AllowedGranularities);
    }

    // The legible granularity pairing for a custom span: fine buckets for short windows, coarse for
    // long ones, so the chart is never a single bar or ten thousand points. First tuple item is the
    // default; the list is the allowed set (offered in the granularity dropdown).
    public static (ThroughputGranularity Default, IReadOnlyList<ThroughputGranularity> Allowed) ForSpan(TimeSpan span)
    {
        var hours = span.TotalHours;
        var days = span.TotalDays;
        if (hours <= 2) return (FiveMinute, new[] { Minute, FiveMinute, TenMinute });
        if (hours <= 12) return (TenMinute, new[] { FiveMinute, TenMinute, Hour });
        if (days <= 2) return (Hour, new[] { TenMinute, Hour });
        if (days <= 14) return (Hour, new[] { Hour, Day });
        if (days <= 92) return (Day, new[] { Day, Week });
        if (days <= 366) return (Week, new[] { Day, Week, Month });
        if (days <= 1100) return (Month, new[] { Week, Month, Quarter });
        return (Quarter, new[] { Month, Quarter, Year });
    }

    public static ThroughputGranularity ResolveGranularity(DashboardRangeOption option, string? granularity) =>
        ResolveGranularity(option.DefaultGranularity, option.AllowedGranularities, granularity);

    public static ThroughputGranularity ResolveGranularity(
        ThroughputGranularity defaultGranularity,
        IReadOnlyList<ThroughputGranularity> allowed,
        string? granularity)
    {
        // Enum.TryParse accepts any integer string ("999" parses to an undefined value), so the
        // IsDefined check matters; the Contains check then enforces the pairing.
        if (granularity is not null
            && Enum.TryParse<ThroughputGranularity>(granularity, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && allowed.Contains(parsed))
        {
            return parsed;
        }

        return defaultGranularity;
    }

    public static string Describe(ThroughputGranularity granularity) => granularity switch
    {
        Second => "per second",
        Minute => "per minute",
        FiveMinute => "per 5 minutes",
        TenMinute => "per 10 minutes",
        Hour => "per hour",
        Day => "per day",
        Week => "per week",
        Month => "per month",
        Quarter => "per quarter",
        Year => "per year",
        _ => "per bucket",
    };

    // --- Calendar boundaries, all in UTC (now is UTC) -------------------------------------------

    // The most recent Monday 00:00 UTC at or before now (ISO week starts Monday).
    private static DateTime StartOfWeek(DateTime now)
    {
        var daysSinceMonday = ((int)now.Date.DayOfWeek + 6) % 7; // Mon→0 … Sun→6
        return now.Date.AddDays(-daysSinceMonday);
    }

    private static DateTime StartOfMonth(DateTime now) =>
        new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime StartOfQuarter(DateTime now) =>
        new(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime StartOfYear(DateTime now) =>
        new(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
