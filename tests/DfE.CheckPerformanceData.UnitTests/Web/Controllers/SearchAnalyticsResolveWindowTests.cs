using System.Globalization;
using System.Reflection;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Unit-level coverage of SearchAnalyticsController.ResolveWindow's custom-range branch.
//
// The custom range is driven by two <input type="date"> controls, so the bound `to` value
// is midnight at the START of the chosen end day. Every analytics query uses an exclusive
// upper bound (occurred_at_utc < @to), so `to` has to be read as an INCLUSIVE end date and
// converted to the following midnight — otherwise the whole of the chosen end day is
// silently missing from the results.
//
// ResolveWindow is internal and the Web assembly doesn't expose internals to this project,
// so we invoke via reflection — same approach as the sibling aggregate-merge test.
public sealed class SearchAnalyticsResolveWindowTests
{
    private static readonly MethodInfo ResolveMethod = typeof(SearchAnalyticsController)
        .GetMethod(
            "ResolveWindow",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "Expected internal static SearchAnalyticsController.ResolveWindow to exist.");

    private static (DateTime FromUtc, DateTime ToUtc, string RangeKey) Resolve(
        string? range, DateTime? from, DateTime? to)
    {
        var result = ResolveMethod.Invoke(null, [range, from, to]);
        return Assert.IsType<ValueTuple<DateTime, DateTime, string>>(result);
    }

    private static DateTime Day(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CustomRange_IncludesTheWholeOfTheChosenEndDay()
    {
        // The admin picked 1 June -> 3 June. All three days must be in the window, so the
        // exclusive upper bound has to land on 4 June midnight, not 3 June midnight.
        var (fromUtc, toUtc, rangeKey) = Resolve("custom", Day(2026, 6, 1), Day(2026, 6, 3));

        Assert.Equal("custom", rangeKey);
        Assert.Equal(Day(2026, 6, 1), fromUtc);
        Assert.Equal(Day(2026, 6, 4), toUtc);
    }

    [Fact]
    public void CustomRange_SameDayInBothFields_ResolvesToThatSingleDay_NotTheSevenDayDefault()
    {
        // Picking one day in both boxes used to make from >= to, which silently snapped the
        // whole dashboard back to the 7-day default — wrong data, and no error to explain it.
        var (fromUtc, toUtc, rangeKey) = Resolve("custom", Day(2026, 6, 1), Day(2026, 6, 1));

        Assert.Equal("custom", rangeKey);
        Assert.Equal(Day(2026, 6, 1), fromUtc);
        Assert.Equal(Day(2026, 6, 2), toUtc);
    }

    [Fact]
    public void CustomRange_SurvivesADrillInRoundTripWithoutDrifting()
    {
        // Drill-in links rebuild the querystring from the RESOLVED window. Re-resolving that
        // querystring must land on the same window, or every click through a drill-in would
        // walk the end date forward a day at a time.
        var first = Resolve("custom", Day(2026, 6, 1), Day(2026, 6, 3));

        var qs = SearchAnalyticsRangeQuery.Build(first.RangeKey, first.FromUtc, first.ToUtc);
        var (rangeKey, from, to) = ParseRangeQuery(qs);

        var second = Resolve(rangeKey, from, to);

        Assert.Equal(first.FromUtc, second.FromUtc);
        Assert.Equal(first.ToUtc, second.ToUtc);
        Assert.Equal(first.RangeKey, second.RangeKey);
    }

    [Fact]
    public void CustomRange_EndDateInTheFuture_IsStillClampedToNow()
    {
        // The end-day widening must not defeat the existing clamp — a hand-edited future
        // date still can't push the upper bound past the present.
        var before = DateTime.UtcNow;
        var (_, toUtc, rangeKey) = Resolve("custom", Day(2026, 6, 1), before.Date.AddDays(30));
        var after = DateTime.UtcNow;

        Assert.Equal("custom", rangeKey);
        Assert.InRange(toUtc, before, after);
    }

    // Mirrors the model binder: pulls range/from/to back out of the querystring the
    // drill-in helper emits.
    private static (string RangeKey, DateTime? From, DateTime? To) ParseRangeQuery(string qs)
    {
        string? range = null, from = null, to = null;
        foreach (var pair in qs.Split('&'))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2) continue;
            var value = Uri.UnescapeDataString(split[1]);
            switch (split[0])
            {
                case "range": range = value; break;
                case "from": from = value; break;
                case "to": to = value; break;
            }
        }

        return (
            range ?? "7d",
            from is null ? null : ParseBound(from),
            to is null ? null : ParseBound(to));
    }

    private static DateTime ParseBound(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
