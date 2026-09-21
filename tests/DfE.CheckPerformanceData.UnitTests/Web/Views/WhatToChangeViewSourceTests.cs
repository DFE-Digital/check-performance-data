namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors LayoutViewSourceTests):
// reads Views/WhatToChange/Index.cshtml from disk and asserts static Razor-source facts for #439.
//
// The Include radio must sit inside the same window-type guard the Add radio already uses, so the
// option cannot be reintroduced outside it. The guard reads IncludeJourney.SupportedWindowTypes —
// the single source of truth — and the Merge/Remove/Add items and the legend must stay unchanged
// (FR-001, FR-002, FR-003, FR-006, FR-008).
public sealed class WhatToChangeViewSourceTests
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
    public void WhatToChange_guards_the_include_radio_on_window_type()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        // The Include item must be conditional on the window type (FR-002, FR-008).
        Assert.Contains("IncludeJourney.SupportedWindowTypes.Contains(", source);
    }

    [Fact]
    public void WhatToChange_keeps_the_merge_remove_and_add_options()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        Assert.Contains("value=\"@WhatToChange.Merge\"", source);
        Assert.Contains("value=\"@WhatToChange.Remove\"", source);
        Assert.Contains("AddPupilJourney.SupportedWindowTypes.Contains(windowType)", source);
    }

    [Fact]
    public void WhatToChange_keeps_the_legend_hint_and_name()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        Assert.Contains("What @Model.Noun.Singular data would you like to change?", source);
        Assert.Contains("Select one option.", source);
        Assert.Contains("for=\"SelectedWhatToChange\"", source);
    }

    [Fact]
    public void WhatToChange_does_not_mention_post16()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        // The gate is a positive whitelist consulted via IncludeJourney.SupportedWindowTypes,
        // never a `== Post16` literal — a new window type must not silently inherit one behaviour.
        Assert.DoesNotContain("Post16", source);
    }
}