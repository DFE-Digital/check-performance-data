using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: the summary page serves both an amendment and a results enquiry. These pin the enquiry
// branch's copy and — just as importantly — that the amendment branch is still there.
public sealed class EnquirySummaryViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "Summary.cshtml"));

    [Fact]
    public void The_enquiry_heading_names_the_student_and_carries_the_check_details_caption()
    {
        var view = ViewSource();

        Assert.Contains("<span class=\"govuk-caption-xl\">Check details</span>", view);
        Assert.Contains("Summary of result enquiry for @Model.PupilName", view);
    }

    [Fact]
    public void The_browser_title_does_not_carry_the_student_name()
    {
        // ViewBag.Title reaches analytics.
        var view = ViewSource();

        Assert.Contains("ViewBag.Title = Model.IsResultsEnquiry ? \"Summary of result enquiry\"", view);
        Assert.DoesNotContain("ViewBag.Title = $\"Summary of result enquiry for", view);
    }

    [Fact]
    public void The_amendment_heading_is_unchanged()
    {
        // The live KS4 journeys share this view.
        var view = ViewSource();

        Assert.Contains("<h1 class=\"govuk-heading-xl\">Summary of amendment request</h1>", view);
        Assert.Contains("Check details for the @Model.WhatToChangeNoun of @Model.PupilName", view);
    }

    [Fact]
    public void An_enquiry_offers_no_draft_saving()
    {
        // Decided: enquiries have no drafts. The button must be absent rather than present-and-broken.
        var view = ViewSource();

        var saveIndex = view.IndexOf("Save and continue later", StringComparison.Ordinal);
        Assert.True(saveIndex > 0, "The amendment journey still needs its draft button.");
        Assert.Contains("@if (!Model.IsResultsEnquiry)", view);
    }

    [Fact]
    public void An_enquiry_can_be_submitted()
        => Assert.Contains("<govuk-button type=\"submit\">Submit request</govuk-button>", ViewSource());

    [Fact]
    public void An_enquiry_offers_a_cancel_link_that_discards_the_journey()
    {
        // AB#298229: the link must point at Cancel, not Index. Index clears nothing, so with the
        // old target a "cancelled" enquiry stayed resumable — and submittable — via a deep link
        // back to the summary URL.
        var view = ViewSource();

        Assert.Contains("Cancel and go back to create a new enquiry", view);
        Assert.Contains("asp-controller=\"ResultIssue\"", view);
        Assert.Contains("asp-action=\"Cancel\"", view);
    }

    [Fact]
    public void An_enquiry_does_not_render_the_evidence_files_section()
    {
        // There is no evidence upload on the enquiry journey, so the section would always be empty.
        var view = ViewSource();

        var guardIndex = view.IndexOf("@if (!Model.IsResultsEnquiry)", StringComparison.Ordinal);
        var evidenceIndex = view.IndexOf("_SummaryEvidenceFiles", StringComparison.Ordinal);
        Assert.True(guardIndex > 0 && evidenceIndex > guardIndex);
    }

    [Fact]
    public void The_rows_still_come_from_the_shared_summary_details_partial()
    {
        // One renderer for both shapes: only the row data differs, via SummaryViewModel.Lines.
        Assert.Contains("<partial name=\"_SummaryDetails\" model=\"Model\" />", ViewSource());
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
