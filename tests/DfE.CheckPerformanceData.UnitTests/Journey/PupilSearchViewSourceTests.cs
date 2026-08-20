using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins how PupilSearch.cshtml asks for a restricted search and how it explains one.
///
/// A results enquiry lists only students the school holds a result for. That hides students, so
/// the page has to say so — otherwise a school cannot tell a typo from "this student has no
/// results". The hint comes from the flow config; the no-match wording is the view's own, because
/// accessible-autocomplete renders it.
/// </summary>
public sealed class PupilSearchViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "PupilSearch.cshtml"));

    [Fact]
    public void The_suggestions_request_carries_the_pages_results_restriction()
    {
        var view = ViewSource();

        Assert.Contains("var requireResults = @(Model.RequireResults ? \"true\" : \"false\");", view);
        Assert.Contains("if (requireResults) url += '&requireResults=true';", view);
    }

    [Fact]
    public void A_restricted_search_says_why_a_student_may_be_missing_when_nothing_matches()
    {
        // The component's default is a bare "No results found", which reads as "you typed it wrong".
        var view = ViewSource();

        Assert.Contains("tNoResults:", view);
        Assert.Contains("No students found with results", view);
    }

    [Fact]
    public void An_unrestricted_search_keeps_the_standard_no_results_wording()
    {
        // The KS4 journeys search the whole roll, where the only reason for no match is the query.
        Assert.Contains("'No results found'", ViewSource());
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
