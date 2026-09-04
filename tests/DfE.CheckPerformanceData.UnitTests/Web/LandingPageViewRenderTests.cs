using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// AB#298317: static Razor-source assertions for Views/LandingPage/Index.cshtml, in the hostless
// style of CheckYourPupilDataViewRenderTests. The page is not reachable in E2E (impersonated users
// have no organisation claim and are challenged), so these and LandingPageControllerTests are the
// whole pin on the closed-window banner and the per-exercise card copy.
public sealed class LandingPageViewRenderTests
{
    private static string ReadView()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views", "LandingPage", "Index.cshtml"));
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void The_card_never_prints_the_outer_window_dates()
    {
        var view = ReadView();

        Assert.DoesNotContain("openWindow.EndTime", view);
        Assert.DoesNotContain("openWindow.EndDate", view);
        Assert.DoesNotContain("openWindow.StartDate", view);
    }

    [Fact]
    public void The_open_deadline_sentence_and_the_closed_range_sentence_are_alternatives()
    {
        var view = ReadView();
        var pupilData = Section(view, "@if (openWindow.PupilDataEndDate is not null)", "@if (openWindow.IsResultsEnquiryOpen)");

        var open = Section(pupilData, "if (openWindow.IsPupilDataOpen)", "else");
        Assert.Contains("You have until @openWindow.PupilDataEndTime on @openWindow.PupilDataEndDate to review your data.", open);

        var closed = pupilData[(pupilData.IndexOf("else", StringComparison.Ordinal))..];
        Assert.Contains("You can request amendments to @openWindow.LearnerNoun.Singular data from @openWindow.PupilDataRangeStart to @openWindow.PupilDataRangeEnd.", closed);
    }

    [Fact]
    public void The_enquiry_sentence_renders_only_while_results_enquiry_is_open()
    {
        var view = ReadView();
        var enquiry = Section(view, "@if (openWindow.IsResultsEnquiryOpen)", "<div>");

        Assert.Contains("You can continue to use this service to raise enquiries about exam results until @openWindow.ResultsEnquiryEndDate.", enquiry);
        // Spelled correctly — the Figma's "enquires" is FLAGGED in the PR, not reproduced.
        Assert.DoesNotContain("enquires", view);
    }

    [Fact]
    public void The_closed_banner_renders_per_window_after_the_existing_two()
    {
        var view = ReadView();

        var noData = view.IndexOf("@foreach (var window in Model.NoDataWindows)", StringComparison.Ordinal);
        var notValid = view.IndexOf("@foreach (var window in Model.NotValidWindows)", StringComparison.Ordinal);
        var closed = view.IndexOf("@foreach (var window in Model.ClosedWindows)", StringComparison.Ordinal);
        var heading = view.IndexOf("<h1 class=\"govuk-heading-xl\">", StringComparison.Ordinal);

        Assert.True(noData >= 0 && notValid > noData && closed > notValid && heading > closed,
            "closed banners must follow the two existing banner loops and precede the h1");
    }

    [Fact]
    public void The_closed_banner_carries_the_designed_sentences_and_scopes_the_next_opportunity()
    {
        var view = ReadView();
        var banner = Section(view, "@foreach (var window in Model.ClosedWindows)", "<h1 class=\"govuk-heading-xl\">");

        Assert.Contains("govuk-notification-banner", banner);
        Assert.Contains("The @window.Title data checking window has closed.", banner);

        var nextOpportunity = Section(banner, "@if (window.NextOpportunity is not null)", "}");
        Assert.Contains("The next opportunity to review your data is in @window.NextOpportunity. We'll email you about a month before.", nextOpportunity);

        Assert.Contains("window.IsResultsEnquiryOpen", banner);
        Assert.Contains("You can still view your exam results and report any issues.", banner);
        Assert.Contains("You can still view and download your", banner);
    }

    private static string Section(string view, string fromMarker, string toMarker)
    {
        var start = view.IndexOf(fromMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"marker not found: {fromMarker}");
        var end = view.IndexOf(toMarker, start + fromMarker.Length, StringComparison.Ordinal);
        return end < 0 ? view[start..] : view[start..end];
    }
}
