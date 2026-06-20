using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// One selectable dashboard window: the query-string value, its display label, the window span,
// and the bucket sizes that make sense for it. The pairings keep a chart legible — a one-hour
// window in day buckets is a single meaningless bar, and a seven-day window in minute buckets
// is ten thousand unreadable points.
public sealed record DashboardRangeOption(
    string Value,
    string Label,
    TimeSpan Window,
    ThroughputGranularity DefaultGranularity,
    IReadOnlyList<ThroughputGranularity> AllowedGranularities,
    bool SinceMidnight = false)
{
    // The window start for this option relative to now: a "today" option counts from midnight UTC
    // (so it grows through the day — "today" means since midnight), every other option is a rolling
    // window ending at now.
    public DateTime From(DateTime nowUtc) => SinceMidnight ? nowUtc.Date : nowUtc - Window;
}

// The server-side allow-list behind the dashboard's range/granularity form. Both selections
// resolve against it before any query runs, so a hand-edited query string can never reach the
// aggregation SQL: an unknown range or an unpaired granularity snaps to a safe default rather
// than erroring — the form is a GET and must always render a dashboard.
public static class DashboardRanges
{
    public const string DefaultValue = "today";

    public static readonly IReadOnlyList<DashboardRangeOption> All = new[]
    {
        // "Today" — since midnight UTC — sits at the top and is the default; its window grows
        // through the day, so it is bucketed hourly (or per 10 minutes early in the day).
        new DashboardRangeOption("today", "Today", TimeSpan.FromHours(24),
            ThroughputGranularity.Hour,
            new[] { ThroughputGranularity.TenMinute, ThroughputGranularity.Hour },
            SinceMidnight: true),
        new DashboardRangeOption("1h", "Last hour", TimeSpan.FromHours(1),
            ThroughputGranularity.Minute,
            new[] { ThroughputGranularity.Minute, ThroughputGranularity.FiveMinute }),
        new DashboardRangeOption("6h", "Last 6 hours", TimeSpan.FromHours(6),
            ThroughputGranularity.FiveMinute,
            new[] { ThroughputGranularity.FiveMinute, ThroughputGranularity.TenMinute, ThroughputGranularity.Hour }),
        new DashboardRangeOption("24h", "Last 24 hours", TimeSpan.FromHours(24),
            ThroughputGranularity.Hour,
            new[] { ThroughputGranularity.TenMinute, ThroughputGranularity.Hour }),
        new DashboardRangeOption("7d", "Last 7 days", TimeSpan.FromDays(7),
            ThroughputGranularity.Day,
            new[] { ThroughputGranularity.Hour, ThroughputGranularity.Day }),
    };

    public static DashboardRangeOption Resolve(string? range) =>
        All.FirstOrDefault(o => string.Equals(o.Value, range, StringComparison.OrdinalIgnoreCase))
        ?? All.Single(o => o.Value == DefaultValue);

    public static ThroughputGranularity ResolveGranularity(DashboardRangeOption option, string? granularity)
    {
        // Enum.TryParse accepts any integer string ("999" parses to an undefined value), so the
        // IsDefined check matters; the Contains check then enforces the per-range pairing.
        if (granularity is not null
            && Enum.TryParse<ThroughputGranularity>(granularity, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && option.AllowedGranularities.Contains(parsed))
        {
            return parsed;
        }

        return option.DefaultGranularity;
    }

    public static string Describe(ThroughputGranularity granularity) => granularity switch
    {
        ThroughputGranularity.Second => "per second",
        ThroughputGranularity.Minute => "per minute",
        ThroughputGranularity.FiveMinute => "per 5 minutes",
        ThroughputGranularity.TenMinute => "per 10 minutes",
        ThroughputGranularity.Hour => "per hour",
        ThroughputGranularity.Day => "per day",
        _ => "per bucket",
    };
}
