using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// AB#296648: pins the entry page's markup and copy against the Figma frames (p-147621 / p-147638).
// Every string here is what a school reads, so a change should be a deliberate content decision
// rather than an accident.
public sealed class ResultIssueViewSourceTests
{
    private static string ViewSource()
        => File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "ResultIssue", "Index.cshtml"));

    [Fact]
    public void The_heading_is_the_radio_legend_so_there_is_exactly_one_h1()
    {
        var view = ViewSource();

        Assert.Contains("is-page-heading=\"true\"", view);
        Assert.Contains("What issue with the results do you need to report?", view);
        // A separate <h1> alongside a page-heading legend would give the page two.
        Assert.DoesNotContain("<h1", view);
    }

    [Fact]
    public void It_uses_the_govuk_radios_component()
    {
        var view = ViewSource();

        Assert.Contains("<govuk-radios for=\"IssueType\">", view);
        Assert.Contains("<govuk-radios-fieldset>", view);
    }

    [Fact]
    public void The_hint_matches_the_design()
    {
        Assert.Contains("<govuk-radios-hint>\r\n                Select one option\r\n            </govuk-radios-hint>".Replace("\r\n", "\n"),
            ViewSource().Replace("\r\n", "\n"));
    }

    [Fact]
    public void The_incorrect_grade_label_is_pinned_verbatim()
    {
        Assert.Contains(
            "Incorrect grade (once you have checked your second late results file in late November)",
            ViewSource());
    }

    [Fact]
    public void The_view_renders_the_incorrect_grade_and_missing_qualification_options_only()
    {
        // "Result does not belong to pupil" still has no journey (sibling ticket) — rendering it
        // would dead-end the user, so it stays off the page.
        var view = ViewSource();

        Assert.Equal(2, view.Split("<govuk-radios-item").Length - 1);
        Assert.Contains("Missing qualification", view);
        Assert.DoesNotContain("Result does not belong to pupil", view);
    }

    [Fact]
    public void The_error_summary_anchors_the_radio_group()
    {
        // The GDS pattern: the summary link must move focus to the first control in the group.
        var view = ViewSource();

        Assert.Contains("<govuk-error-summary-item href=\"#issueType\">", view);
        Assert.Contains("<govuk-error-summary-title>There is a problem</govuk-error-summary-title>", view);
        // The anchor only resolves because the id is set explicitly rather than generated.
        Assert.Contains("id=\"issueType\"", view);
    }

    [Fact]
    public void An_inline_error_message_accompanies_the_summary()
    {
        // GDS requires both: the summary at the top and the message beside the field.
        Assert.Contains("<govuk-radios-error-message>", ViewSource());
    }

    [Fact]
    public void The_late_results_expander_is_present_with_the_designs_summary_text()
    {
        var view = ViewSource();

        Assert.Contains("<details class=\"govuk-details\" data-module=\"govuk-details\">", view);
        Assert.Contains(
            "<span class=\"govuk-details__summary-text\">Have you checked your second late results file?</span>",
            view);
    }

    [Fact]
    public void The_expander_sits_after_continue_so_it_reads_as_guidance_not_a_gate()
    {
        var view = ViewSource();

        Assert.True(
            view.IndexOf("<govuk-button type=\"submit\">Continue</govuk-button>", StringComparison.Ordinal)
            < view.IndexOf("<details class=\"govuk-details\"", StringComparison.Ordinal),
            "The details expander must come after the Continue button, as the Figma screen shows.");
    }

    [Fact]
    public void The_form_posts_to_the_confirm_action_with_an_antiforgery_token()
    {
        var view = ViewSource();

        Assert.Contains("method=\"post\"", view);
        Assert.Contains("Url.Action(\"Confirm\", \"ResultIssue\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
    }

    [Fact]
    public void The_back_link_returns_to_the_pupil_data_page()
    {
        Assert.Contains(
            "<a asp-action=\"Index\" asp-controller=\"CheckYourPupilData\" asp-route-windowId=\"@Model.WindowId\" class=\"govuk-back-link\">Back</a>",
            ViewSource());
    }

    [Fact]
    public void The_page_works_without_javascript()
    {
        // Progressive enhancement is mandatory: radios plus a submit button, no script.
        Assert.DoesNotContain("<script", ViewSource());
    }

    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
        }
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
