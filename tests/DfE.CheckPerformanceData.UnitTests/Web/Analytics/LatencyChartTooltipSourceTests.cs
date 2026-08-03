namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

// Source-text assertions pinning the wiring that keeps the latency-percentiles chart's
// hover tooltip aligned with what the chart actually draws.
//
// The visible circles on the latency chart sit at each bucket's p50 y-coordinate (see
// _LatencyChart.cshtml — only p50 gets circles). If the crosshair tooltip announced only
// the sibling p95 value (the historic single-value shape), an admin hovering a p50 dot
// near 1 ms would see "55 ms (p95)" and reasonably conclude the chart is lying.
//
// The fix bakes all three percentile arrays into the SVG as data-* attributes and the
// bucket-mode crosshair renders one line per series when they're present, so the tooltip
// content matches whichever line the cursor is near. No JS test runner is wired, so
// these assertions guard the contract at the source level — same pattern as
// ObservabilityBoardSourceTests.
public sealed class LatencyChartTooltipSourceTests
{
    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string WebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepoRoot, "src", "DfE.CheckPerformanceData.Web" }.Concat(parts).ToArray()));

    private static string LatencyChart() => WebFile("Views", "Admin", "Search", "_LatencyChart.cshtml");
    private static string CrosshairJs() => WebFile("wwwroot", "js", "search-analytics.js");
    private static string Css() => WebFile("wwwroot", "css", "search-analytics.css");

    [Fact]
    public void LatencyChart_BakesAllThreePercentileArraysOntoTheSvg()
    {
        var view = LatencyChart();

        Assert.Contains("data-sa-values-p5=", view);
        Assert.Contains("data-sa-values-p50=", view);
        Assert.Contains("data-sa-values-p95=", view);

        // Every array must actually be populated from the bucket data (not a placeholder).
        Assert.Contains("b.P5Ms.ToString(",  view);
        Assert.Contains("b.P50Ms.ToString(", view);
        Assert.Contains("b.P95Ms.ToString(", view);
    }

    [Fact]
    public void LatencyChart_ValueSuffixDoesNotClaimP95Alone()
    {
        var view = LatencyChart();

        // Historic bug: suffix was " ms (p95)" so a tooltip built from the sibling p95
        // array read as authoritative even when the hovered dot was a p50 circle.
        Assert.DoesNotContain("data-sa-value-suffix=\" ms (p95)\"", view);
        Assert.Contains("data-sa-value-suffix=\" ms\"", view);
    }

    [Fact]
    public void CrosshairJs_ParsesTheThreePercentileArrays_AndBranchesOnHasPercentileSeries()
    {
        var js = CrosshairJs();

        Assert.Contains("data-sa-values-p5",  js);
        Assert.Contains("data-sa-values-p50", js);
        Assert.Contains("data-sa-values-p95", js);
        Assert.Contains("hasPercentileSeries", js);

        // The multi-series branch must produce a p5 / p50 / p95 readout, not a single
        // value. Assert the shape of the tooltip string so a future regression that
        // silently drops one of the three shows up here.
        Assert.Contains("p5 ",  js);
        Assert.Contains("p50 ", js);
        Assert.Contains("p95 ", js);
    }

    [Fact]
    public void TooltipCss_HonoursNewlinesFromTextContent()
    {
        var css = Css();

        // Multi-line tooltip content depends on this whitespace rule. Without it the
        // per-series lines collapse onto a single row and the tooltip regresses back to
        // reading like a single arbitrary value.
        Assert.Contains(".sa-chart__crosshair-tooltip", css);
        Assert.Matches(@"\.sa-chart__crosshair-tooltip\s*\{[^}]*white-space:\s*pre-line", css);
    }
}
