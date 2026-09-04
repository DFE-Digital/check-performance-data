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

    // Ticket copy, verbatim including the "enquires" typo — FLAGGED for content sign-off, but
    // until the BA changes it, drift here is a defect.
    [Fact]
    public void TheEmptyStateCarriesTheTicketCopyVerbatim()
    {
        Assert.Contains("There are no submitted result enquires", ViewSource());
    }

    // The sibling test above only proves the copy exists SOMEWHERE in the file — a flipped
    // condition (e.g. "@if (Model.HasAnyIssues)") would still pass it. A flip like that tells a
    // school with enquiries "there are none" and sends them off to raise the duplicate this page
    // exists to prevent, and nothing else catches it: the always-on CI build gate excludes E2E
    // (that job needs the `deploy` label). So this pin is branch-scoped: it locates the literal
    // negated condition and asserts the copy sits inside THAT branch, not just in the file.
    [Fact]
    public void TheEmptyStateOnlyRendersInsideTheNoIssuesBranch()
    {
        var source = ViewSource();
        var ifIndex = source.IndexOf("@if (!Model.HasAnyIssues)", StringComparison.Ordinal);
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
        Assert.DoesNotContain("@if (Model.HasAnyIssues)", noIssuesBranch);
    }

    // A search is a safe, repeatable read: it must be a GET so refresh/back/bookmark work and no
    // antiforgery token is involved. (The app 404s unmatched verbs, so a POST form would break
    // outright — this pins the cheaper-to-read source instead.)
    [Fact]
    public void TheSearchFormIsAGet()
    {
        var source = ViewSource();
        var formIndex = source.IndexOf("results-enquiries-search-form", StringComparison.Ordinal);
        Assert.True(formIndex >= 0, "The Issues search form (id results-enquiries-search-form) is missing.");
        Assert.Contains("method=\"get\"", source[..source.IndexOf("</form>", formIndex, StringComparison.Ordinal)][formIndex..]);
    }

    // The form action carries the #results-enquiries fragment so the tabs component re-selects the Results Enquiries
    // tab after the round trip; without it a search dumps the user back on the Requests tab.
    [Fact]
    public void TheSearchFormReturnsToTheIssuesTab()
    {
        Assert.Contains("/AmendmentRequests#results-enquiries", ViewSource());
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
    // Scoped to the table markup deliberately, not the whole panel: a "Clear search" link (parked
    // as a BA question, see PR notes) may legitimately land in the panel — outside the table —
    // later, and a whole-panel anchor-free pin would break the moment that ships.
    [Fact]
    public void TheIssuesTabOffersNoRowActions()
    {
        var source = ViewSource();
        var issuesStart = source.IndexOf("<govuk-tabs-item id=\"results-enquiries\"", StringComparison.Ordinal);
        var issuesPanel = source[issuesStart..source.IndexOf("</govuk-tabs>", StringComparison.Ordinal)];
        var tableStart = issuesPanel.IndexOf("<table", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "The Issues table markup is missing.");
        var tableEnd = issuesPanel.IndexOf("</table>", tableStart, StringComparison.Ordinal) + "</table>".Length;
        var issuesTable = issuesPanel[tableStart..tableEnd];
        Assert.DoesNotContain("<a", issuesTable);
        // Belt and braces for the whole panel: the anchor check above cannot see a link emitted
        // at runtime (e.g. @Html.ActionLink), but any route to the enquiry-hostile
        // SubmittedRequest controller has to name it in source. A future "Clear search" link
        // (parked BA question) points at AmendmentRequests, so this stays safe.
        Assert.DoesNotContain("SubmittedRequest", issuesPanel);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
