namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// Source-file assertion pattern (mirrors LayoutViewSourceTests / AutocompleteRestoreViewSourceTests):
// reads Web-project source files from disk and asserts static facts for GitHub issue #271 —
// pupil page responsive display fixes. On narrow viewports the pupil search box collapsed to a
// few characters wide (flex row squeezed by the Search/Clear buttons) and the pupil tables
// overflowed off-screen (no horizontal-scroll container).
public sealed class CheckYourPupilDataResponsiveViewSourceTests
{
    private static string ReadWebSource(string relativePath)
    {
        var webDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(webDir, relativePath));
    }

    [Fact]
    public void Site_css_stacks_pupil_search_row_below_the_mobile_breakpoint()
    {
        // Normalise line endings so the assertion is identical on Windows and the Linux CI runner.
        var css = ReadWebSource("wwwroot/css/site.css").Replace("\r\n", "\n");

        // The stacked block must exist verbatim: the same mobile-breakpoint fix .cypmd-search__row
        // already has. If someone reformats site.css this pins the selector to the breakpoint.
        Assert.Contains("@media (max-width: 40.0625em) {\n  .pupil-search__row {", css);

        // And the stack must be a column with full-width children, not just any media rule.
        var mediaIndex = css.IndexOf("@media (max-width: 40.0625em) {\n  .pupil-search__row {", StringComparison.Ordinal);
        var block = css[mediaIndex..(mediaIndex + 400)];
        Assert.Contains("flex-direction: column", block);
        Assert.Contains("align-items: stretch", block);
    }

    [Fact]
    public void Pupil_section_wraps_the_table_in_an_accessible_scrollable_pane()
    {
        var view = ReadWebSource("Views/CheckYourPupilData/_PupilSection.cshtml");

        // MOJ scrollable pane (CSS ships in moj-frontend-9.0.0.min.css, already linked by
        // _Layout) gives wide KS4 tables horizontal scroll instead of off-screen overflow.
        Assert.Contains("moj-scrollable-pane", view);

        // The scroll region must be keyboard-reachable and announceable: tabindex lets
        // keyboard users scroll it (WCAG 2.1.1), role+label give it an accessible name.
        Assert.Contains("tabindex=\"0\"", view);
        Assert.Contains("role=\"region\"", view);
        Assert.Contains("aria-label=\"@Model.Section.Heading\"", view);

        // The pane must open before the table partial renders, i.e. it wraps the table.
        var paneIndex = view.IndexOf("moj-scrollable-pane", StringComparison.Ordinal);
        var tableIndex = view.IndexOf("_PupilTable", StringComparison.Ordinal);
        Assert.True(paneIndex >= 0 && paneIndex < tableIndex,
            "the moj-scrollable-pane container must wrap the _PupilTable partial");
    }
}
