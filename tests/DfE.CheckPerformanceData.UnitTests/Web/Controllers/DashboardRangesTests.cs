using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The dashboard's window/bucket selection is resolved against a server-side allow-list:
// a recognised preset window paired with the bucket sizes that make sense for it. Anything
// unrecognised snaps to a safe default rather than erroring — the form is a GET and a stale
// or hand-edited query string must always render a dashboard.
public sealed class DashboardRangesTests
{
    [Theory]
    [InlineData("1h")]
    [InlineData("6h")]
    [InlineData("24h")]
    [InlineData("7d")]
    public void Resolve_ReturnsTheMatchingPreset(string range)
    {
        var option = DashboardRanges.Resolve(range);

        Assert.Equal(range, option.Value);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var option = DashboardRanges.Resolve("7D");

        Assert.Equal("7d", option.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fortnight")]
    [InlineData("25h")]
    public void Resolve_UnknownRange_FallsBackToTheDefault(string? range)
    {
        var option = DashboardRanges.Resolve(range);

        Assert.Equal(DashboardRanges.DefaultValue, option.Value);
    }

    // "Today" is the default and the first option, and it counts from midnight rather than a
    // rolling window — "today" means since midnight, so it grows through the day.
    [Fact]
    public void Default_IsTodaySinceMidnight()
    {
        Assert.Equal("today", DashboardRanges.DefaultValue);

        var today = DashboardRanges.Resolve(null);
        Assert.Equal("today", today.Value);
        Assert.True(today.IsToday);
    }

    // The select lists windows shortest first, longest last, with the open-ended custom range at the
    // bottom — so both the charting range dropdown and the seed time-frame dropdown read in order.
    [Fact]
    public void All_AreOrderedShortestToLongest_CustomLast()
    {
        var expected = new[]
        {
            "1h", "6h", "today", "yesterday", "24h",
            "thisweek", "lastweek", "7d",
            "thismonth", "lastmonth",
            "thisquarter", "lastquarter",
            "thisyear", "lastyear",
            "custom",
        };

        Assert.Equal(expected, DashboardRanges.All.Select(o => o.Value).ToArray());
        Assert.Equal("custom", DashboardRanges.All[^1].Value);
    }

    [Fact]
    public void TodayRange_FromIsMidnightUtc_OtherRangesAreRolling()
    {
        var now = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);

        var (todayFrom, todayTo) = DashboardRanges.Resolve("today").Window(now);
        Assert.Equal(new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc), todayFrom);
        Assert.Equal(now, todayTo);

        var (rollingFrom, rollingTo) = DashboardRanges.Resolve("24h").Window(now);
        Assert.Equal(now - TimeSpan.FromHours(24), rollingFrom);
        Assert.Equal(now, rollingTo);
    }

    // --- Named calendar ranges resolve to calendar-aligned windows (UTC) ---

    [Theory]
    [InlineData("yesterday")]
    [InlineData("thisweek")]
    [InlineData("lastweek")]
    [InlineData("thismonth")]
    [InlineData("lastmonth")]
    [InlineData("thisquarter")]
    [InlineData("lastquarter")]
    [InlineData("thisyear")]
    [InlineData("lastyear")]
    [InlineData("custom")]
    public void Resolve_ReturnsTheMatchingNamedRange(string range)
    {
        Assert.Equal(range, DashboardRanges.Resolve(range).Value);
    }

    [Fact]
    public void CalendarRanges_ComputeCalendarAlignedWindows()
    {
        // A Saturday afternoon: 2026-06-20 14:30 UTC. Week starts Monday (2026-06-15).
        var now = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

        var yesterday = DashboardRanges.Resolve("yesterday").Window(now);
        Assert.Equal((Utc(2026, 6, 19), Utc(2026, 6, 20)), yesterday);

        var thisWeek = DashboardRanges.Resolve("thisweek").Window(now);
        Assert.Equal(Utc(2026, 6, 15), thisWeek.From);
        Assert.Equal(now, thisWeek.To);

        var lastWeek = DashboardRanges.Resolve("lastweek").Window(now);
        Assert.Equal((Utc(2026, 6, 8), Utc(2026, 6, 15)), lastWeek);

        var thisMonth = DashboardRanges.Resolve("thismonth").Window(now);
        Assert.Equal(Utc(2026, 6, 1), thisMonth.From);

        var lastMonth = DashboardRanges.Resolve("lastmonth").Window(now);
        Assert.Equal((Utc(2026, 5, 1), Utc(2026, 6, 1)), lastMonth);

        var thisQuarter = DashboardRanges.Resolve("thisquarter").Window(now);
        Assert.Equal(Utc(2026, 4, 1), thisQuarter.From); // Q2 starts April

        var lastQuarter = DashboardRanges.Resolve("lastquarter").Window(now);
        Assert.Equal((Utc(2026, 1, 1), Utc(2026, 4, 1)), lastQuarter);

        var thisYear = DashboardRanges.Resolve("thisyear").Window(now);
        Assert.Equal(Utc(2026, 1, 1), thisYear.From);

        var lastYear = DashboardRanges.Resolve("lastyear").Window(now);
        Assert.Equal((Utc(2025, 1, 1), Utc(2026, 1, 1)), lastYear);
    }

    // --- Custom range: from/to from the query, guarded and clamped, span-derived granularity ---

    [Fact]
    public void ResolveWindow_Custom_UsesSuppliedFromAndTo_ToDefaultsToNow()
    {
        var now = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        var from = new DateTime(2026, 6, 18, 9, 0, 0, DateTimeKind.Utc);

        var resolved = DashboardRanges.ResolveWindow("custom", from, null, now);

        Assert.Equal("custom", resolved.Option.Value);
        Assert.Equal(from, resolved.From);
        Assert.Equal(now, resolved.To); // to defaults to now
        Assert.Contains(resolved.DefaultGranularity, resolved.AllowedGranularities);
    }

    [Fact]
    public void ResolveWindow_Custom_InvertedRange_FallsBackToASaneSpan()
    {
        var now = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        var from = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc); // after to
        var to = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);

        var resolved = DashboardRanges.ResolveWindow("custom", from, to, now);

        Assert.True(resolved.From < resolved.To);
    }

    [Fact]
    public void ResolveWindow_Custom_OverlongSpan_IsClampedToTheMax()
    {
        var now = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        var from = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = now;

        var resolved = DashboardRanges.ResolveWindow("custom", from, to, now);

        Assert.True(resolved.To - resolved.From <= DashboardRanges.MaxCustomSpan);
    }

    [Theory]
    [InlineData(1, ThroughputGranularity.FiveMinute)]      // ≤2h
    [InlineData(8, ThroughputGranularity.TenMinute)]       // ≤12h
    [InlineData(36, ThroughputGranularity.Hour)]           // ≤2d
    [InlineData(24 * 30, ThroughputGranularity.Day)]       // ≤92d
    [InlineData(24 * 200, ThroughputGranularity.Week)]     // ≤366d
    [InlineData(24 * 800, ThroughputGranularity.Month)]    // ≤1100d
    [InlineData(24 * 2000, ThroughputGranularity.Quarter)] // longer
    public void ForSpan_PicksALegibleDefaultGranularity(int hours, ThroughputGranularity expected)
    {
        var (def, allowed) = DashboardRanges.ForSpan(TimeSpan.FromHours(hours));
        Assert.Equal(expected, def);
        Assert.Contains(def, allowed);
    }

    [Fact]
    public void ResolveGranularity_CustomAllowedList_RejectsAnUnpairedValue()
    {
        // A year-long custom span pairs week/month/quarter — minute must snap to the default.
        var (def, allowed) = DashboardRanges.ForSpan(TimeSpan.FromDays(200));
        Assert.Equal(def, DashboardRanges.ResolveGranularity(def, allowed, "Minute"));
        Assert.Equal(ThroughputGranularity.Month, DashboardRanges.ResolveGranularity(def, allowed, "month"));
    }

    [Fact]
    public void EveryRequestedGranularity_IsReachableFromSomeRangeOrSpan()
    {
        // The R7 menu: per minute / 10 min / hour / day / week / month / quarter / year are all
        // selectable somewhere (the finer ones via named ranges, year via a long custom span).
        var reachable = DashboardRanges.All.SelectMany(o => o.AllowedGranularities)
            .Concat(DashboardRanges.ForSpan(TimeSpan.FromDays(2000)).Allowed)
            .Distinct()
            .ToHashSet();

        foreach (var g in new[]
        {
            ThroughputGranularity.Minute, ThroughputGranularity.TenMinute, ThroughputGranularity.Hour,
            ThroughputGranularity.Day, ThroughputGranularity.Week, ThroughputGranularity.Month,
            ThroughputGranularity.Quarter, ThroughputGranularity.Year,
        })
        {
            Assert.Contains(g, reachable);
        }
    }

    [Fact]
    public void ResolveGranularity_NullOrMissing_UsesTheRangeDefault()
    {
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, null));
        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, ""));
    }

    [Fact]
    public void ResolveGranularity_AcceptsAPairedValue_CaseInsensitively()
    {
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(
            ThroughputGranularity.TenMinute,
            DashboardRanges.ResolveGranularity(option, "tenminute"));
    }

    [Fact]
    public void ResolveGranularity_UnpairedValue_SnapsToTheRangeDefault()
    {
        // Day buckets over a one-hour window would render a single meaningless bar.
        var option = DashboardRanges.Resolve("1h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, "Day"));
    }

    [Fact]
    public void ResolveGranularity_NumericEnumSmuggling_SnapsToTheRangeDefault()
    {
        // Enum.TryParse accepts any integer string, so "999" parses to an undefined value;
        // the allow-list must reject it before it reaches the bucket-spec mapping.
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, "999"));
    }

    [Fact]
    public void EveryRange_PairsItsOwnDefaultGranularity()
    {
        Assert.All(DashboardRanges.All, option =>
            Assert.Contains(option.DefaultGranularity, option.AllowedGranularities));
    }

    [Fact]
    public void EveryGranularityLabel_IsHumanReadable()
    {
        Assert.All(DashboardRanges.All, option =>
            Assert.All(option.AllowedGranularities, g =>
                Assert.False(string.IsNullOrWhiteSpace(DashboardRanges.Describe(g)))));
    }
}
