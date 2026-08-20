using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648 / AB#297130: pins the "Incorrect grade details" page and the grade picker partial.
public sealed class ResultDetailsViewSourceTests
{
    private static string View(string name) =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", name));

    private static string Page() => View("ResultDetails.cshtml");
    private static string Partial() => View("_GradeSelect.cshtml");

    // ── The page ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_page_shows_which_result_is_being_corrected()
    {
        // The user is about to change a grade; they must be able to see it is the right one.
        var view = Page();

        foreach (var row in new[]
                 {
                     "Student name", "CYPMD ID", "Qualification number (QAN)",
                     "Qualification name and subject", "Session", "Current grade"
                 })
        {
            Assert.Contains($"<govuk-summary-list-row-key>{row}</govuk-summary-list-row-key>", view);
        }
    }

    [Fact]
    public void The_late_results_reminder_is_pinned_verbatim()
    {
        Assert.Contains(
            "Before you report an incorrect grade, check the second late results (issued in November) to",
            Page());
        Assert.Contains("make sure the grade is not included there.", Page());
    }

    [Fact]
    public void The_reminder_is_inset_text_after_the_picker_not_a_precondition()
    {
        var view = Page();

        Assert.Contains("govuk-inset-text", view);
        Assert.True(
            view.IndexOf("_Question", StringComparison.Ordinal) < view.IndexOf("govuk-inset-text", StringComparison.Ordinal),
            "The reminder must come after the grade picker, as the Figma screen shows.");
    }

    [Fact]
    public void The_page_renders_an_error_summary_and_a_back_link()
    {
        var view = Page();

        Assert.Contains("_JourneyErrorSummary", view);
        Assert.Contains("_JourneyBackLink", view);
    }

    [Fact]
    public void The_page_posts_to_the_generic_page_action()
    {
        // ResultDetails is a question page with a different view, so it reuses PagePost's answer
        // handling rather than duplicating it.
        var view = Page();

        Assert.Contains("Url.Action(\"PagePost\", \"Journey\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
    }

    // ── The grade picker ─────────────────────────────────────────────────────

    [Fact]
    public void The_picker_is_a_real_select_so_the_page_works_without_javascript()
    {
        var view = Partial();

        Assert.Contains("<select class=\"govuk-select", view);
        Assert.Contains("@foreach (var option in Model.VisibleOptions)", view);
    }

    [Fact]
    public void The_placeholder_option_has_an_empty_value_and_the_designs_text()
    {
        Assert.Contains("<option value=\"\">Select revised grade</option>", Partial());
    }

    [Fact]
    public void Nothing_is_preselected_unless_the_user_already_chose_it()
    {
        Assert.Contains("selected=\"@(option.Value == Model.ExistingAnswer?.TextValue)\"", Partial());
    }

    [Fact]
    public void The_field_name_follows_the_journey_convention()
    {
        // PagePost reads answers by q_{questionId with dashes as underscores}.
        var view = Partial();

        Assert.Contains("id=\"@Model.FieldName\" name=\"@Model.FieldName\"", view);
    }

    [Fact]
    public void A_missing_grade_scale_is_explained_rather_than_shown_as_an_empty_control()
    {
        var view = Partial();

        Assert.Contains("Model.GradeOptionsUnavailable", view);
        Assert.Contains("We cannot list grades for this qualification yet", view);
    }

    [Fact]
    public void The_describedby_chain_covers_hint_unavailable_and_error()
    {
        // Naming a missing element leaves a dangling reference, so each id is conditional on being
        // rendered.
        var view = Partial();

        Assert.Contains("Model.Question.Hint is not null ? hintId : null", view);
        Assert.Contains("Model.GradeOptionsUnavailable ? unavailableId : null", view);
        Assert.Contains("Model.HasError ? errorId : null", view);
    }

    [Fact]
    public void The_inline_error_carries_the_visually_hidden_error_prefix()
    {
        var view = Partial();

        Assert.Contains("<span class=\"govuk-visually-hidden\">Error:</span>", view);
        Assert.Contains("class=\"govuk-error-message\" id=\"@errorId\"", view);
    }

    [Fact]
    public void The_select_is_enhanced_into_a_searchable_control()
    {
        // Some qualifications award 93 grades — far too many to scan in a plain dropdown.
        var view = Partial();

        Assert.Contains("accessibleAutocomplete.enhanceSelectElement({", view);
        Assert.Contains("showAllValues: true", view);
    }

    [Fact]
    public void The_enhancement_never_auto_picks_a_grade()
    {
        // 24F and 24D differ by one character; auto-selecting the first match could turn a fail into
        // a pass without the user noticing.
        var view = Partial();

        Assert.Contains("autoselect: false", view);
        Assert.Contains("confirmOnBlur: false", view);
    }

    [Fact]
    public void The_enhancement_is_skipped_when_there_is_nothing_to_choose()
    {
        var view = Partial();

        Assert.Contains("select.options.length <= 1", view);
        Assert.Contains("typeof accessibleAutocomplete === 'undefined'", view);
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
