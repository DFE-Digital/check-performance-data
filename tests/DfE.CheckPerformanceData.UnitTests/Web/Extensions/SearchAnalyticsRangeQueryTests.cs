using System.Globalization;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.UnitTests.Web.Extensions;

// Pins the shape the drill-in views assume:
//   * preset (7d / 30d / …) → "range=<key>" (no from/to)
//   * custom               → "range=custom&from=<iso>&to=<yyyy-MM-dd>"
//
// The custom branch's bounds are the ones ResolveWindow expects. `from` is an inclusive
// instant and round-trips as ISO-8601. `to` is the inclusive end DATE — ResolveWindow
// widens it to the following midnight to build the exclusive query bound, so the link
// must carry the end date rather than the widened bound or each drill-in click would
// push the window one day further out.
public sealed class SearchAnalyticsRangeQueryTests
{
    [Theory]
    [InlineData("7d")]
    [InlineData("24h")]
    [InlineData("30d")]
    [InlineData("90d")]
    public void Build_Preset_EmitsOnlyRangeKey(string preset)
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var qs = SearchAnalyticsRangeQuery.Build(preset, from, to);

        Assert.Equal($"range={preset}", qs);
        Assert.DoesNotContain("from=", qs);
        Assert.DoesNotContain("to=",   qs);
    }

    [Fact]
    public void Build_Custom_EmitsRangeCustomAndBothIsoBounds()
    {
        var from = new DateTime(2026, 5, 15, 8, 30, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 7, 20, 17, 45, 0, DateTimeKind.Utc);

        var qs = SearchAnalyticsRangeQuery.Build("custom", from, to);

        Assert.Contains("range=custom", qs);
        Assert.Contains("from=", qs);
        Assert.Contains("to=",   qs);
    }

    [Fact]
    public void Build_Custom_EmitsBoundsTheModelBinderParsesBack()
    {
        // Regression: drill-in links used to emit only ?range=<key> even for custom, so
        // the controller's "custom needs both from and to" guard fell through and snapped
        // to the 7-day default. Verify both bounds are present and parseable, `from`
        // bit-for-bit as a UTC instant and `to` as the inclusive end date.
        var from = new DateTime(2026, 5, 15, 8, 30, 0, DateTimeKind.Utc);
        // Exclusive upper bound covering everything up to the end of 20 July.
        var to   = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

        var qs = SearchAnalyticsRangeQuery.Build("custom", from, to);

        var parts = qs.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]));

        Assert.Equal("custom", parts["range"]);

        var fromParsed = DateTime.Parse(parts["from"], CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        Assert.Equal(DateTimeKind.Utc, fromParsed.Kind);
        Assert.Equal(from, fromParsed);

        // The end date the admin picked — not the widened exclusive bound.
        Assert.Equal("2026-07-20", parts["to"]);
    }

    [Fact]
    public void InclusiveEndDate_ConvertsTheExclusiveBoundBackToTheChosenDay()
    {
        // Midnight-aligned bound: the day before.
        Assert.Equal(
            new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            SearchAnalyticsRangeQuery.InclusiveEndDate(
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)));

        // Clamped-to-now bound (mid-day): that same day, since "now" still falls inside it.
        Assert.Equal(
            new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            SearchAnalyticsRangeQuery.InclusiveEndDate(
                new DateTime(2026, 7, 20, 14, 15, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Build_NullOrEmptyRangeKey_DefaultsTo7d()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("range=7d", SearchAnalyticsRangeQuery.Build("", from, to));
        Assert.Equal("range=7d", SearchAnalyticsRangeQuery.Build(null!, from, to));
    }
}
