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
        Assert.Contains("<govuk-tabs-item id=\"results-enquiries\" label=\"Results Enquiries\">", source);
    }

    // The tab is gated on the window running a results-enquiry checking exercise. Asserting only
    // that the condition exists somewhere in the file would miss the tab being moved out of it, so
    // this pins the tab markup INSIDE the branch.
    [Fact]
    public void TheIssuesTabOnlyRendersForAWindowWithAResultsEnquiryExercise()
    {
        var source = ViewSource();
        var ifIndex = source.IndexOf("@if (Model.ShowResultsEnquiries)", StringComparison.Ordinal);
        Assert.True(ifIndex >= 0, "The Results Enquiries tab's exercise condition is missing or changed.");
        var tabIndex = source.IndexOf("<govuk-tabs-item id=\"results-enquiries\"", StringComparison.Ordinal);
        Assert.True(tabIndex > ifIndex, "The Results Enquiries tab is rendered outside its exercise condition.");
    }

    // Ticket copy, verbatim including the "enquires" typo — FLAGGED for content sign-off, but
    // until the BA changes it, drift here is a defect.
    [Fact]
    public void TheEmptyStateCarriesTheTicketCopyVerbatim()
    {
        Assert.Contains("There are no submitted result enquires", ViewSource());
    }

    // The sibling test above only proves the copy exists SOMEWHERE in the file — a flipped
    // condition (e.g. "@if (Model.IssueRows.Count > 0)") would still pass it. A flip like that
    // tells a school with enquiries "there are none" and sends them off to raise the duplicate this
    // page exists to prevent, and nothing else catches it: the always-on CI build gate excludes E2E
    // (that job needs the `deploy` label). So this pin is branch-scoped: it locates the literal
    // empty condition and asserts the copy sits inside THAT branch, not just in the file.
    [Fact]
    public void TheEmptyStateOnlyRendersInsideTheNoIssuesBranch()
    {
        var source = ViewSource();
        var ifIndex = source.IndexOf("@if (Model.IssueRows.Count == 0)", StringComparison.Ordinal);
        Assert.True(ifIndex >= 0, "The Issues tab's \"no issues at all\" branch condition is missing or changed.");
        var elseIndex = source.IndexOf("else", ifIndex, StringComparison.Ordinal);
        Assert.True(elseIndex >= 0, "Expected an else branch after the no-issues condition.");
        var noIssuesBranch = source[ifIndex..elseIndex];
        Assert.Contains("There are no submitted result enquires", noIssuesBranch);
        // The "else" landmark above is the FIRST else after the condition, which is only the
        // branch's own terminator while the if/else structure stands. If the pair is refactored
        // into two sibling @if blocks, the slice silently runs on to some nested else and can
        // swallow the has-issues markup — so a slice that contains the positive condition proves
        // the landmark has slipped, not that the copy is in the right branch. If this fires on an
        // intentional restructure, re-anchor the slice; do not delete the assertion.
        Assert.DoesNotContain("@if (Model.IssueRows.Count > 0)", noIssuesBranch);
    }

    // The search box was removed: the tab lists every enquiry, so there is nothing to filter and
    // no GET round trip to land back on the tab. Its return would bring the round trip with it.
    [Fact]
    public void TheIssuesTabHasNoSearchForm()
    {
        var panel = ResultsEnquiriesPanel();
        Assert.DoesNotContain("<form", panel);
        Assert.DoesNotContain("resultsEnquiriesSearch", panel);
        Assert.DoesNotContain("type=\"search\"", panel);
    }

    // The results-enquiry deadline is stated beside the enquiries it governs, on this tab — not
    // in the page header, which now carries the pupil-data deadline alone. Only the submit-by
    // sentence (past tense once closed) is stated: a submitted results enquiry cannot be edited,
    // so the "You can edit your ... requests" line the Requests tab carries must not appear here.
    [Fact]
    public void TheIssuesTabStatesTheResultsEnquiryDeadline()
    {
        var panel = ResultsEnquiriesPanel();
        Assert.Contains("@enquiryDeadline.Sentence", panel);
        Assert.DoesNotContain("You can edit your", panel);
    }

    // The header inset and the Requests tab list every deadline BUT results enquiry: a results
    // enquiry stated there would be stated twice, and the Requests tab's "window is closed" line
    // would stay hidden by an open results enquiry after pupil data had shut.
    [Fact]
    public void TheHeaderAndRequestsTabStateOnlyTheRequestDeadlines()
    {
        var source = ViewSource();
        var beforeIssuesTab = source[..source.IndexOf("<govuk-tabs-item id=\"results-enquiries\"", StringComparison.Ordinal)];
        Assert.DoesNotContain("Model.Deadlines", beforeIssuesTab);
        Assert.Contains("@foreach (var deadline in Model.RequestDeadlines)", beforeIssuesTab);
        Assert.Contains("@foreach (var deadline in Model.RequestDeadlines.Where(d => d.IsOpen))", beforeIssuesTab);
        Assert.Contains("@if (Model.RequestDeadlines.All(d => !d.IsOpen))", beforeIssuesTab);
    }

    [Fact]
    public void TheIssuesTableHasTheDesignedColumns()
    {
        var source = ViewSource();
        Assert.Contains(">CYPMD id</th>", source);
        Assert.Contains(">Type</th>", source);
        Assert.Contains(">Qualification</th>", source);
    }

    // AC 4 (separation): the Issues TABLE renders enquiry fields only, with no per-row actions —
    // reusing the Requests rows' view/delete links here would resurrect the broken enquiry
    // View/Delete surface that hiding enquiries from the Requests tab was meant to avoid.
    // Scoped to the table markup deliberately, not the whole panel, so a link elsewhere in the
    // panel does not break the pin.
    [Fact]
    public void TheIssuesTabOffersNoRowActions()
    {
        var issuesPanel = ResultsEnquiriesPanel();
        var tableStart = issuesPanel.IndexOf("<table", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "The Issues table markup is missing.");
        var tableEnd = issuesPanel.IndexOf("</table>", tableStart, StringComparison.Ordinal) + "</table>".Length;
        var issuesTable = issuesPanel[tableStart..tableEnd];
        Assert.DoesNotContain("<a", issuesTable);
        // Belt and braces for the whole panel: the anchor check above cannot see a link emitted
        // at runtime (e.g. @Html.ActionLink), but any route to the enquiry-hostile
        // SubmittedRequest controller has to name it in source.
        Assert.DoesNotContain("SubmittedRequest", issuesPanel);
    }

    private static string ResultsEnquiriesPanel()
    {
        var source = ViewSource();
        var issuesStart = source.IndexOf("<govuk-tabs-item id=\"results-enquiries\"", StringComparison.Ordinal);
        Assert.True(issuesStart >= 0, "The Results Enquiries tab is missing.");
        return source[issuesStart..source.IndexOf("</govuk-tabs>", StringComparison.Ordinal)];
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
