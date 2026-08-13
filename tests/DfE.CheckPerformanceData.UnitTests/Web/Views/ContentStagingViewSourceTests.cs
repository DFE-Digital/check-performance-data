namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors LayoutViewSourceTests): reads the content-staging view
// from disk and asserts where the reset-content markup sits relative to its guard.
//
// The controller refuses the clear-all route outside a dev surface, and a companion test covers
// that. This one covers the other half: that the button, the confirm modal and the form action
// are all INSIDE the guard. Rendering an actionable destructive control that the route will 404
// is not a security hole, but it is a support call — an administrator on QA clicking a button
// that appears to work and silently does nothing. Nothing in the compiler or in a controller
// test notices if someone later moves that markup out of the conditional, which is exactly the
// edit this guards.
public sealed class ContentStagingViewSourceTests
{
    private const string ViewPath = "Views/ContentStaging/Index.cshtml";

    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    [Fact]
    public void ResetContentSection_IsRenderedOnlyBehindTheClearAllGuard()
    {
        var source = ReadViewSource(ViewPath);

        var guard = source.IndexOf("@if (ViewData[\"ClearAllAvailable\"] is true)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "the reset-content section must be guarded by the ClearAllAvailable flag");

        // Everything that makes the control actionable has to come after the guard opens.
        foreach (var marker in new[]
                 {
                     // The heading markup, not the words: the guard's own comment mentions
                     // "reset content" and would otherwise match here.
                     "<h2 class=\"govuk-heading-l\">Reset content</h2>",
                     "clear-all-modal",
                     "/admin/content-staging/clear-all",
                     "Clear all CMS content"
                 })
        {
            var at = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(at > guard,
                $"'{marker}' appears at {at}, before the guard at {guard} — it would render on every environment");
        }
    }

    // The import and export halves of this page are ordinary admin functions and must not be
    // caught by the guard: gating them would take content staging away from QA and preproduction,
    // which is where bundles are actually replayed.
    [Fact]
    public void ImportAndExport_AreNotGated()
    {
        var source = ReadViewSource(ViewPath);

        var guard = source.IndexOf("@if (ViewData[\"ClearAllAvailable\"] is true)", StringComparison.Ordinal);
        Assert.True(guard >= 0);

        foreach (var marker in new[] { "/admin/content-staging/preview", "Preview import" })
        {
            var at = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(at >= 0 && at < guard,
                $"'{marker}' should sit outside the clear-all guard, but was found at {at} (guard at {guard})");
        }
    }
}
