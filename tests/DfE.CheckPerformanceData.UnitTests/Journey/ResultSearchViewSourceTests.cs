using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: pins the ResultSearch page's markup (Figma p-147877 / p-147913).
//
// The property that matters most is that this page works with JavaScript off: the options are
// server-rendered into a real <select>, not fetched, which would leave a script-less browser with an
// empty control. The accessible-autocomplete enhancement this control once carried was removed —
// the list is short enough to read — so the select is now all there is.
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
        // Same helper the suggestions endpoint uses, so this page and that endpoint cannot describe
        // the same result differently.
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
    public void The_select_carries_no_javascript_enhancement()
    {
        // The type-ahead is gone: a student holds a handful of results, so the list is read rather
        // than searched. The no-JS guarantee below is unaffected — it was always the same <select>.
        var view = ViewSource();

        Assert.DoesNotContain("<script", view);
        Assert.DoesNotContain("accessibleAutocomplete", view);
        // Guards against a regression to a fetch-only autocomplete, which would break the no-JS path.
        Assert.DoesNotContain("/results/suggestions", view);
    }

    [Fact]
    public void The_heading_labels_the_control()
    {
        var view = ViewSource();

        Assert.Contains("<label class=\"govuk-label govuk-label--l\" for=\"result-search\">@Model.Title</label>", view);
        Assert.Contains("<h1 class=\"govuk-label-wrapper\">", view);
    }

    [Fact]
    public void The_hint_describes_the_list_rather_than_a_search()
        // The design's "Start typing to search" described the type-ahead this page no longer has.
        // FLAGGED: copy needs content sign-off.
        => Assert.Contains("Results are listed by subject and QAN", ViewSource());

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
        // Model.Title contains the student's name and the <title> reaches analytics, so the title
        // comes from the flow config's name-free pageTitle — not hardcoded (AB#298704: this page
        // now serves two journeys, and a hardcoded "Which result is incorrect?" mislabelled the
        // result-does-not-belong page; same fix QualificationSearch.cshtml already carries).
        var view = ViewSource();

        Assert.Contains("ViewBag.Title = Model.PageTitle ?? \"Which result is incorrect?\"", view);
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

    // ── Empty state: a student the school holds no results for ───────────────

    [Fact]
    public void A_student_with_no_results_gets_an_explanation_rather_than_an_empty_control()
    {
        // Rendering an empty dropdown leaves the user with a control that can never answer.
        // Mirrors _GradeSelect.cshtml, which states a missing-reference-data gap plainly.
        var view = ViewSource();

        Assert.Contains("if (!Model.AvailableResults.Any())", view);
        Assert.Contains("govuk-inset-text", view);
        Assert.Contains("We hold no results for this student", view);
    }

    [Fact]
    public void The_empty_state_offers_a_way_back_to_the_student_search()
    {
        // The only move left is to pick a different student, so the page has to offer it — the
        // Continue button would only ever fail validation.
        var view = ViewSource();

        Assert.Contains("Search for a different student", view);
        Assert.DoesNotContain("href=\"#\"", view);
    }

    [Fact]
    public void The_control_and_the_continue_button_are_hidden_when_there_is_nothing_to_choose()
    {
        // A submit that can only ever produce "Enter which result is incorrect" is a dead end.
        var view = ViewSource();

        // The submit and the select live in the else branch of the empty check, so neither is
        // rendered when there is nothing to pick.
        Assert.Matches(
            """if \(!Model\.AvailableResults\.Any\(\)\)[\s\S]*else[\s\S]*<govuk-button type="submit">""",
            view);
    }

    [Fact]
    public void The_view_renders_content_as_inset_text_only_when_present()
    {
        var view = ViewSource();

        Assert.Contains("Model.Content is not null", view);
        Assert.Contains("govuk-inset-text", view);
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
