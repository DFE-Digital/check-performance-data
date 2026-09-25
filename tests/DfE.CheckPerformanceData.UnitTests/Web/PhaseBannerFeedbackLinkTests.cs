using DfE.CheckPerformanceData.Web.Settings;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The Beta phase banner's "feedback" link opens the feedback survey in a new tab. The anchor
// still routes through /feedback-link (ContactController.FeedbackLink records the click as a
// feedback_clicked event, then redirects to the survey), so these pin the survey URL the
// redirect defaults to and the anchor's new-tab attributes and link text. The view facts are
// static Razor-source assertions, same pattern as AccessibilityAuditViewTests; the redirect
// itself is covered by ContactControllerTests.
public sealed class PhaseBannerFeedbackLinkTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string ReadLayout()
    {
        // Repo root is three levels up from tests/DfE.CheckPerformanceData.UnitTests/Web/.
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_Layout.cshtml"));
    }

    // The phase-banner anchor, from "<a" to "</a>".
    private static string FeedbackAnchor()
    {
        var view = ReadLayout();
        var start = view.IndexOf("<a class=\"govuk-link\" href=\"/feedback-link\"", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "_Layout.cshtml must keep the phase-banner link pointing at /feedback-link so the click is tracked.");
        var end = view.IndexOf("</a>", start, StringComparison.Ordinal);
        return view[start..(end + "</a>".Length)];
    }

    [Fact]
    public void Settings_DefaultToTheLiveSurvey()
    {
        Assert.Equal("https://forms.cloud.microsoft/e/NtJTefhXHz", new FeedbackSurveySettings().Url);
    }

    [Fact]
    public void PhaseBannerLink_OpensInANewTab()
    {
        var anchor = FeedbackAnchor();

        Assert.Contains("target=\"_blank\"", anchor);
    }

    [Fact]
    public void PhaseBannerLink_SaysItOpensInANewTab()
    {
        // GOV.UK Design System: a link that opens in a new tab says so in its link text.
        var anchor = FeedbackAnchor();

        Assert.EndsWith(">feedback (opens in new tab)</a>", anchor);
    }

    [Fact]
    public void PhaseBannerLink_UsesNoopenerButKeepsTheReferer()
    {
        // "noreferrer" would strip the Referer header that RefererPagePath reads for the
        // feedback_clicked event's page_path, so every click would log page_path = null.
        var anchor = FeedbackAnchor();

        Assert.Contains("rel=\"noopener\"", anchor);
        Assert.DoesNotContain("noreferrer", anchor);
    }
}
