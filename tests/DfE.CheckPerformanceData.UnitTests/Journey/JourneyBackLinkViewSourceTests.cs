using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// The shared journey Back link. Three things can be behind "Back": the summary, an earlier page in
// the history, or — on a journey's very first page, where there is no history — the chooser the
// journey started from. AB#296648 added a second chooser, so that last case is now a branch: an
// amendment journey began at WhatToChange, a results enquiry at ResultIssue, and sending the user to
// the wrong one drops them into a different task entirely.
public sealed class JourneyBackLinkViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "_JourneyBackLink.cshtml"));

    [Fact]
    public void An_enquiry_first_page_goes_back_to_the_result_issue_chooser()
    {
        var view = ViewSource();

        // Model.IsResultsEnquiry, not a named enum member: the branch used to test IncorrectGrade
        // alone, so the missing-qualification journey fell through to the amendment chooser
        // (AB#297848). PageViewModelResultsEnquiryTests covers which journeys that property is true
        // for; this only pins that the view asks the question that way.
        Assert.Contains("Model.IsResultsEnquiry", view);
        Assert.Contains("asp-controller=\"ResultIssue\"", view);
        Assert.DoesNotContain("WhatToChange.IncorrectGrade", view);
    }

    [Fact]
    public void An_amendment_first_page_still_goes_back_to_the_what_to_change_chooser()
    {
        // The live KS4 journeys must be unaffected.
        Assert.Contains("asp-controller=\"WhatToChange\"", ViewSource());
    }

    [Fact]
    public void A_page_with_history_uses_the_resolved_action_for_the_previous_page()
    {
        // A bool cannot express three routes; pointing at the wrong action 404s.
        var view = ViewSource();

        Assert.Contains("asp-action=\"@Model.BackPageAction\"", view);
        Assert.DoesNotContain("BackPageIsPupilSearch ?", view);
    }

    [Fact]
    public void Coming_from_the_summary_returns_to_the_summary()
    {
        var view = ViewSource();

        Assert.Contains("Model.FromSummary", view);
        Assert.Contains("asp-action=\"Summary\"", view);
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
