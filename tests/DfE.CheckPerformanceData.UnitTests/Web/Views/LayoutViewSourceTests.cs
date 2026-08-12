namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors SearchIndexViewTests / AutocompleteRestoreViewSourceTests):
// reads Views/Shared/_Layout.cshtml from disk and asserts static Razor-source facts for
// AB#286387 R18/R19/R23 client-side analytics wiring.
//
// Form-less pages (CMS/guidance) have no __RequestVerificationToken hidden input, so
// _Layout must expose the antiforgery token via a <meta name="request-verification-token">
// tag, which wwwroot/js/test-data-seed.js:42 already probes for as a fallback and which
// the new wwwroot/js/analytics-events.js also relies on. _Layout must also load the new
// script. The href="#" assertion is a regression guard for the dead feedback link removed
// in Task 4.
public sealed class LayoutViewSourceTests
{
    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    [Fact]
    public void Layout_exposes_antiforgery_meta_and_loads_analytics_events_js()
    {
        var source = ReadViewSource("Views/Shared/_Layout.cshtml");

        Assert.Contains("request-verification-token", source);
        Assert.Contains("analytics-events.js", source);
        Assert.DoesNotContain("href=\"#\"", source); // the dead feedback link is gone (Task 4)
    }
}
