using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: pins the ResultSearch page's markup (Figma p-147877 / p-147913).
//
// The property that matters most is that this page works with JavaScript off. CLAUDE.md makes
// progressive enhancement mandatory, so the options are server-rendered into a real <select> that
// accessible-autocomplete upgrades in place — not fetched, which would leave a script-less browser
// with an empty control.
public sealed class ResultSearchViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "ResultSearch.cshtml"));

    [Fact]
    public void The_control_is_a_real_select_so_the_page_works_without_javascript()
    {
        var view = ViewSource();

        Assert.Contains("<select class=\"govuk-select", view);
        Assert.Contains("id=\"result-search\" name=\"selectedResultKey\"", view);
        Assert.Contains("@foreach (var result in Model.AvailableResults)", view);
    }

    [Fact]
    public void Each_option_carries_the_composite_key_as_its_value()
    {
        // The QAN alone cannot identify a result — the same qualification can appear in two sessions.
        Assert.Contains("value=\"@result.CompositeKey\"", ViewSource());
    }

    [Fact]
    public void Option_text_comes_from_the_shared_label_helper()
    {
        // Same helper the suggestions endpoint uses, so the enhanced and unenhanced views of this
        // page cannot describe the same result differently.
        Assert.Contains("ResultLabel.For(result)", ViewSource());
    }

    [Fact]
    public void Nothing_is_preselected()
    {
        var view = ViewSource();

        Assert.Contains("<option value=\"\"></option>", view);
        Assert.Contains("selected=\"@(result.CompositeKey == Model.SelectedResultKey)\"", view);
    }

    [Fact]
    public void The_select_is_enhanced_rather_than_replaced()
    {
        var view = ViewSource();

        Assert.Contains("accessibleAutocomplete.enhanceSelectElement({", view);
        Assert.Contains("selectElement: select", view);
        // Guards against a regression to a fetch-only autocomplete, which would break the no-JS path.
        Assert.DoesNotContain("/results/suggestions", view);
    }

    [Fact]
    public void The_enhancement_never_auto_picks_a_result()
    {
        // Choosing the wrong result sends the DfE to check a grade the school never queried.
        var view = ViewSource();

        Assert.Contains("autoselect: false", view);
        Assert.Contains("confirmOnBlur: false", view);
    }

    [Fact]
    public void The_script_degrades_quietly_when_the_autocomplete_library_is_absent()
    {
        // Without this the page would throw and leave the (working) select in place but unstyled.
        Assert.Contains("typeof accessibleAutocomplete === 'undefined'", ViewSource());
    }

    [Fact]
    public void The_heading_labels_the_control()
    {
        // enhanceSelectElement moves the select's id onto the new input, so this label names
        // whichever control is visible.
        var view = ViewSource();

        Assert.Contains("<label class=\"govuk-label govuk-label--l\" for=\"result-search\">@Model.Title</label>", view);
        Assert.Contains("<h1 class=\"govuk-label-wrapper\">", view);
    }

    [Fact]
    public void The_hint_matches_the_design()
        => Assert.Contains("Start typing to search for results by subject or QAN", ViewSource());

    [Fact]
    public void The_error_summary_and_inline_error_both_target_the_control()
    {
        var view = ViewSource();

        Assert.Contains("<govuk-error-summary-item href=\"#result-search\">", view);
        Assert.Contains("id=\"result-search-error\" class=\"govuk-error-message\"", view);
        Assert.Contains("<span class=\"govuk-visually-hidden\">Error:</span>", view);
    }

    [Fact]
    public void The_describedby_chain_covers_the_hint_and_the_error()
    {
        Assert.Contains(
            "aria-describedby=\"result-search-hint @(hasError ? \"result-search-error\" : \"\")\"",
            ViewSource());
    }

    [Fact]
    public void The_browser_title_does_not_leak_the_student_name()
    {
        // Model.Title contains the student's name and the <title> reaches analytics.
        var view = ViewSource();

        Assert.Contains("ViewBag.Title = \"Which result is incorrect?\"", view);
        Assert.DoesNotContain("ViewBag.Title = Model.Title", view);
    }

    [Fact]
    public void The_selected_state_shows_the_confirmation_summary_rows()
    {
        // Figma p-147894: once a result is chosen the page proves which one, including the source
        // file — the one thing the label itself cannot disambiguate.
        var view = ViewSource();

        foreach (var row in new[]
                 {
                     "Qualification name and subject", "Qualification number (QAN)",
                     "Syllabus code", "Session", "Current Grade", "CSV file"
                 })
        {
            Assert.Contains($"<govuk-summary-list-row-key>{row}</govuk-summary-list-row-key>", view);
        }
    }

    [Fact]
    public void The_form_posts_to_the_result_search_action_with_an_antiforgery_token()
    {
        var view = ViewSource();

        Assert.Contains("Url.Action(\"ResultSearchPost\", \"Journey\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
    }

    [Fact]
    public void The_back_link_uses_the_resolved_action_rather_than_guessing()
    {
        Assert.Contains("asp-action=\"@Model.BackPageAction\"", ViewSource());
    }

    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
