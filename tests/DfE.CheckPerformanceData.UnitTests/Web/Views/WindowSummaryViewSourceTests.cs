using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the window summary page's close contracts (AB#301022) at a tier the always-on CI gate runs:
// each exercise shows its status, Close is offered only while the exercise is open, and the
// hand-over sweep is offered on its own only once it has closed.
public sealed class WindowSummaryViewSourceTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", "Summary.cshtml"));

    // The text from the first occurrence of `from` up to the next occurrence of `to` after it.
    private static string Between(string view, string from, string to)
    {
        var start = view.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' not found in Summary.cshtml");
        var end = view.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' not found after '{from}' in Summary.cshtml");
        return view[start..end];
    }

    [Fact]
    public void Each_exercise_shows_its_status_as_a_tag()
    {
        var view = View();
        Assert.Contains("<key>Status</key>", view);
        Assert.Contains("<value><strong class=\"govuk-tag @exercise.StatusTagClass\">@exercise.StatusLabel</strong></value>", view);
    }

    [Fact]
    public void Close_is_offered_only_while_the_exercise_is_open()
    {
        // AC1 and AC2. One occurrence of the link, and it sits inside the open branch.
        var view = View();
        Assert.Equal(1, view.Split("@exercise.CloseLink").Length - 1);
        var openBranch = Between(view, "@if (exercise.IsOpen)", "@if (exercise.HasClosed)");
        Assert.Contains("href=\"@exercise.CloseLink\"", openBranch);
        Assert.Contains("Close @exercise.Label", openBranch);
        Assert.Contains("govuk-button--warning", openBranch);
    }

    [Fact]
    public void Sending_requests_is_offered_only_once_the_exercise_has_closed()
    {
        var view = View();
        Assert.Equal(1, view.Split("@exercise.SendRequestsLink").Length - 1);
        var closedBranch = view[view.IndexOf("@if (exercise.HasClosed)", StringComparison.Ordinal)..];
        Assert.Contains("href=\"@exercise.SendRequestsLink\"", closedBranch);
        Assert.Contains("Send @exercise.Label requests for processing", closedBranch);
    }

    [Fact]
    public void Close_does_not_depend_on_the_exercise_having_ingress_files()
    {
        // An exercise with no dataset slots (a results enquiry on a KS2 window) can still be open
        // to schools, so it must still be closable: the buttons sit after the datasets branch.
        var view = View();
        var datasets = view.IndexOf("@if (exercise.Datasets.Count == 0)", StringComparison.Ordinal);
        var buttons = view.IndexOf("<div class=\"govuk-button-group\">", StringComparison.Ordinal);
        var close = view.IndexOf("@if (exercise.IsOpen)", StringComparison.Ordinal);
        Assert.True(datasets >= 0 && buttons > datasets && close > buttons);
        Assert.Contains("@if (exercise.Datasets.Count > 0)", Between(view, "<div class=\"govuk-button-group\">", "@if (exercise.IsOpen)"));
    }

    [Fact]
    public void A_refused_close_is_a_neutral_banner_and_a_completed_one_a_success_banner()
    {
        var view = View();
        Assert.Contains("CloseExerciseController.TempDataKey] is string closeOutcome", view);
        Assert.Contains("govuk-notification-banner govuk-notification-banner--success\" role=\"alert\"", view);
        Assert.Contains("SendExerciseRequestsController.RefusedTempDataKey] is string closeRefused", view);
        // GOV.UK: role="alert" is for a success banner only; a neutral one is a region.
        Assert.Contains("<div class=\"govuk-notification-banner\" role=\"region\"", view);
        Assert.Contains("aria-labelledby=\"close-refused-title\"", view);
        Assert.Contains("id=\"close-refused-title\">Important</h2>", view);
        Assert.Contains("@closeRefused", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
