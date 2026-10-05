using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the close confirmation page's contracts (AB#301022) that the always-on CI gate cannot see at
// E2E tier: what the admin is shown before closing, the type-the-name field and its error state,
// and that the page carries no script.
public sealed class CloseExerciseViewSourceTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", "Close.cshtml"));

    [Fact]
    public void The_page_has_one_h1_and_its_title_matches_it()
    {
        var view = View();
        Assert.Equal(1, view.Split("<h1 ").Length - 1);
        Assert.Contains("ViewBag.Title = $\"Close {Model.ExerciseLabel} early?\";", view);
        Assert.Contains("<h1 class=\"govuk-heading-l\">Close @Model.ExerciseLabel early?</h1>", view);
    }

    [Fact]
    public void The_warning_names_the_scheduled_end_the_consequence_and_that_it_cannot_be_undone()
    {
        // AC3: "its scheduled end, and that the action cannot be undone".
        var view = View();
        Assert.Contains("<govuk-warning-text icon-fallback-text=\"Warning\">", view);
        Assert.Contains("This exercise is due to close on @Model.ScheduledEndDate at @Model.ScheduledEndTime.", view);
        Assert.Contains("@Model.Consequence", view);
        Assert.Contains("You cannot undo this.", view);
    }

    [Fact]
    public void The_summary_shows_the_window_the_exercise_its_status_and_its_scheduled_end()
    {
        // AC3: "I am shown the window name".
        var view = View();
        Assert.Contains("<govuk-summary-list-row-key>Window</govuk-summary-list-row-key>", view);
        Assert.Contains("<govuk-summary-list-row-value>@Model.WindowTitle</govuk-summary-list-row-value>", view);
        Assert.Contains("<govuk-summary-list-row-key>Checking exercise</govuk-summary-list-row-key>", view);
        Assert.Contains("<govuk-summary-list-row-key>Status</govuk-summary-list-row-key>", view);
        Assert.Contains("<strong class=\"govuk-tag govuk-tag--green\">Open</strong>", view);
        Assert.Contains("<govuk-summary-list-row-key>Scheduled end</govuk-summary-list-row-key>", view);
        Assert.Contains("@Model.ScheduledEndDate @Model.ScheduledEndTime", view);
    }

    [Fact]
    public void The_confirmation_field_is_a_labelled_text_input_whose_hint_gives_the_exact_name()
    {
        var view = View();
        Assert.Contains("<label class=\"govuk-label govuk-label--m\" for=\"confirm-window-name\">To confirm, type the window name</label>", view);
        Assert.Contains("<div id=\"confirm-window-name-hint\" class=\"govuk-hint\">Enter exactly: @Model.WindowTitle</div>", view);
        Assert.Contains("id=\"confirm-window-name\" name=\"confirmWindowName\" type=\"text\"", view);
        Assert.Contains("value=\"@Model.ConfirmWindowName\"", view);
        // A name is not something a browser should remember or "correct".
        Assert.Contains("autocomplete=\"off\"", view);
        Assert.Contains("spellcheck=\"false\"", view);
        Assert.Contains("aria-describedby=\"confirm-window-name-hint", view);
    }

    [Fact]
    public void A_mismatch_shows_one_error_summary_linked_to_the_field_and_an_inline_error()
    {
        // AC4. Hand-written markup, so there is exactly one summary — the library's field-error
        // tag helpers can add a second.
        var view = View();
        Assert.Contains("ViewData[\"HasError\"] = Model.HasError;", view);
        Assert.Equal(1, view.Split("class=\"govuk-error-summary\"").Length - 1);
        Assert.Contains("data-module=\"govuk-error-summary\"", view);
        Assert.Contains("<h2 class=\"govuk-error-summary__title\">There is a problem</h2>", view);
        Assert.Contains("<li><a href=\"#confirm-window-name\">@Model.Error</a></li>", view);
        Assert.Contains("<p id=\"confirm-window-name-error\" class=\"govuk-error-message\">", view);
        Assert.Contains("<span class=\"govuk-visually-hidden\">Error:</span> @Model.Error", view);
        Assert.Contains("govuk-form-group--error", view);
        Assert.Contains("govuk-input--error", view);
        Assert.Contains("confirm-window-name-error\" : \"\")", view);
        Assert.DoesNotContain("<govuk-input", view);
        Assert.DoesNotContain("<govuk-error-summary", view);
    }

    [Fact]
    public void The_form_posts_to_the_close_route_with_an_antiforgery_token_and_offers_cancel()
    {
        var view = View();
        Assert.Contains("<form method=\"post\" action=\"@Model.PostUrl\" novalidate>", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("<govuk-button type=\"submit\" class=\"govuk-button--warning\">", view);
        Assert.Contains("Close @Model.ExerciseLabel", view);
        Assert.Contains("<a class=\"govuk-link govuk-!-margin-left-3\" href=\"@Model.CancelLink\">Cancel</a>", view);
        Assert.Contains("<govuk-back-link href=\"@Model.CancelLink\" />", view);
    }

    [Fact]
    public void The_page_says_what_happens_to_requests_but_shows_no_counts_and_no_script()
    {
        var view = View();
        Assert.Contains("Requests schools have already submitted are sent for processing", view);
        Assert.Contains("Drafts a school has not submitted are cancelled", view);
        Assert.DoesNotContain("RequestsToClose", view);
        Assert.DoesNotContain("DraftsToCancel", view);
        Assert.DoesNotContain("<script", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
