using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// The decision pull's confirm page: one heading, the preview sentence, a back link, and the
// warning submit button in an anti-forgery form — the same shape as the send-requests confirm
// page, which is what keeps the admin on the same footing before each irreversible action.
public sealed class PullDecisionsViewSourceTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", "PullDecisions.cshtml"));

    [Fact]
    public void The_page_has_one_heading_naming_the_exercise()
    {
        var view = View();
        Assert.Equal(1, view.Split("<h1 class=\"govuk-heading-l\">").Length - 1);
        Assert.Contains("Pull decisions from Zendesk for @Model.ExerciseLabel</h1>", view);
    }

    [Fact]
    public void The_page_shows_the_preview_counts()
    {
        var view = View();
        Assert.Contains("@Model.SubmittedTotal", view);
        Assert.Contains("@Model.WithTicket", view);
        Assert.Contains("submitted requests will be checked", view);
    }

    [Fact]
    public void The_page_offers_a_cancel_link_to_the_window_summary()
    {
        var view = View();
        Assert.Contains("href=\"@Model.CancelLink\"", view);
    }

    [Fact]
    public void The_submit_button_warns_and_posts_the_anti_forgery_form()
    {
        var view = View();
        Assert.Contains("action=\"@Model.PostUrl\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("govuk-button--warning", view);
        Assert.Contains("type=\"submit\"", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
