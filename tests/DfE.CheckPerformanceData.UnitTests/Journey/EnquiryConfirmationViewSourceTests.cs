using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: the confirmation page (Figma p-148581). Every string is pinned — this is where the school
// gets the reference they will quote to the DfE, so the copy is a contract with the support team as
// much as with the user.
public sealed class EnquiryConfirmationViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "EnquiryConfirmation.cshtml"));

    [Fact]
    public void The_panel_confirms_the_submission_and_shows_the_reference()
    {
        var view = ViewSource();

        Assert.Contains("<govuk-panel-title>Results enquiry submitted</govuk-panel-title>", view);
        Assert.Contains("Your reference number", view);
        Assert.Contains("<strong>@Model.ReferenceNumber</strong>", view);
    }

    [Fact]
    public void The_email_confirmation_sentence_is_pinned()
        => Assert.Contains(
            "We have sent you an email to confirm that you have successfully submitted an enquiry.",
            ViewSource());

    [Fact]
    public void The_onward_links_use_the_shared_labels()
    {
        // #713: the links follow the exercises open now, not the submitted kind, and they carry
        // the check-your-pupil-data page's words so they cannot drift. Routing to ResultIssue is
        // what makes "none of my previous answers are carried over" true — that controller builds
        // a brand-new RequestState.
        var view = ViewSource();

        Assert.Contains("NextStepLabels.For(NextSteps.ResultsEnquiry, Model.LearnerNoun)", view);
        Assert.Contains("NextStepLabels.For(NextSteps.RequestChange, Model.LearnerNoun)", view);
        Assert.Contains("asp-controller=\"ResultIssue\"", view);
        Assert.Contains("asp-controller=\"WhatToChange\"", view);
        Assert.DoesNotContain("Report another issue with an exam result", view);
    }

    [Fact]
    public void The_return_link_is_always_offered()
    {
        var view = ViewSource();

        Assert.Contains("Return to check your data", view);
        Assert.Contains("asp-controller=\"CheckYourPupilData\"", view);
    }

    [Fact]
    public void The_what_happens_next_section_is_pinned()
    {
        var view = ViewSource();

        Assert.Contains("<h2 class=\"govuk-heading-m\">What happens next</h2>", view);
        Assert.Contains("After you submit your changes:", view);
        Assert.Contains("<li>the DfE will review your enquiry</li>", view);
        Assert.Contains("<li>your performance data will be updated in the Spring, if required</li>", view);
    }

    [Fact]
    public void There_is_no_back_link()
    {
        // The journey state is cleared on submission, so Back would lead somewhere that no longer
        // exists — and re-submitting is not something to invite.
        Assert.DoesNotContain("govuk-back-link", ViewSource());
    }

    [Fact]
    public void The_page_title_matches_the_panel()
        => Assert.Contains("ViewBag.Title = \"Results enquiry submitted\"", ViewSource());

    [Fact]
    public void The_page_needs_no_javascript()
        => Assert.DoesNotContain("<script", ViewSource());

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
