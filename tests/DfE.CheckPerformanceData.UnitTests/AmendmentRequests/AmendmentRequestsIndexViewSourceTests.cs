using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// AB#298325: markup contracts for the Issues tab. These read the .cshtml source because the
// cheapest place to catch a copy or method regression is the file itself.
public sealed class AmendmentRequestsIndexViewSourceTests
{
    private static string ViewSource() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "AmendmentRequests", "Index.cshtml"));

    [Fact]
    public void TheIssuesTabExists()
    {
        var source = ViewSource();
        Assert.Contains("<govuk-tabs-item id=\"issues\" label=\"Issues\">", source);
    }

    // Ticket copy, verbatim including the "enquires" typo — FLAGGED for content sign-off, but
    // until the BA changes it, drift here is a defect.
    [Fact]
    public void TheEmptyStateCarriesTheTicketCopyVerbatim()
    {
        Assert.Contains("There are no submitted result enquires", ViewSource());
    }

    // A search is a safe, repeatable read: it must be a GET so refresh/back/bookmark work and no
    // antiforgery token is involved. (The app 404s unmatched verbs, so a POST form would break
    // outright — this pins the cheaper-to-read source instead.)
    [Fact]
    public void TheSearchFormIsAGet()
    {
        var source = ViewSource();
        var formIndex = source.IndexOf("issue-search-form", StringComparison.Ordinal);
        Assert.True(formIndex >= 0, "The Issues search form (id issue-search-form) is missing.");
        Assert.Contains("method=\"get\"", source[..source.IndexOf("</form>", formIndex, StringComparison.Ordinal)][formIndex..]);
    }

    // The form action carries the #issues fragment so the tabs component re-selects the Issues
    // tab after the round trip; without it a search dumps the user back on the Requests tab.
    [Fact]
    public void TheSearchFormReturnsToTheIssuesTab()
    {
        Assert.Contains("/AmendmentRequests#issues", ViewSource());
    }

    [Fact]
    public void TheIssuesTableHasTheDesignedColumns()
    {
        var source = ViewSource();
        Assert.Contains(">CYPMD id</th>", source);
        Assert.Contains(">Type</th>", source);
        Assert.Contains(">Qualification</th>", source);
    }

    // AC 4 (separation): the Issues table renders enquiry fields only — reusing the Requests
    // rows' view/delete links here would resurrect the broken enquiry View/Delete surface that
    // hiding enquiries from the Requests tab was meant to avoid.
    [Fact]
    public void TheIssuesTabOffersNoRowActions()
    {
        var source = ViewSource();
        var issuesStart = source.IndexOf("<govuk-tabs-item id=\"issues\"", StringComparison.Ordinal);
        var issuesPanel = source[issuesStart..source.IndexOf("</govuk-tabs>", StringComparison.Ordinal)];
        Assert.DoesNotContain("SubmittedRequest", issuesPanel);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
