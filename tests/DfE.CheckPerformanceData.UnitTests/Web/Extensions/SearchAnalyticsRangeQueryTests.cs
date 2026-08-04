using System.Globalization;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.UnitTests.Web.Extensions;

// Pins the shape the drill-in views assume:
//   * preset (7d / 30d / …) → "range=<key>" (no from/to)
//   * custom               → "range=custom&from=<iso>&to=<iso>"
//
// The custom branch's from/to values are the ones ResolveWindow expects — ISO-8601
// round-trip format — so a drill-in link built here parses back to the same window.
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
    public void Build_Custom_FromAndToParseBackToTheSameDateTime()
    {
        // Regression: drill-in links used to emit only ?range=<key> even for custom, so
        // the controller's "custom needs both from and to" guard fell through and snapped
        // to the 7-day default. Verify the from/to strings this helper writes are exactly
        // the ISO-8601 round-trip format the ASP.NET model binder parses back to a UTC
        // DateTime bit-for-bit.
        var from = new DateTime(2026, 5, 15, 8, 30, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 7, 20, 17, 45, 0, DateTimeKind.Utc);

        var qs = SearchAnalyticsRangeQuery.Build("custom", from, to);

        var parts = qs.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]));

        Assert.Equal("custom", parts["range"]);

        var fromParsed = DateTime.Parse(parts["from"], CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var toParsed = DateTime.Parse(parts["to"], CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        Assert.Equal(DateTimeKind.Utc, fromParsed.Kind);
        Assert.Equal(DateTimeKind.Utc, toParsed.Kind);
        Assert.Equal(from, fromParsed);
        Assert.Equal(to,   toParsed);
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
