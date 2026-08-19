using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

/// <summary>
/// The way in to a 16-19 results enquiry (AB#296648). Mirrors <see cref="WhatToChangeController"/>:
/// pick what you are reporting, and the journey engine takes it from there.
/// </summary>
public sealed class ResultIssueController(
    ICheckYourPupilDataService service,
    IQuestionFlowService flowService,
    ILateResultsAvailability lateResults,
    ICurrentUserService currentUser,
    IAnalyticsService analytics) : Controller
{
    /// <summary>
    /// Entered when the school does not yet hold a second-late-results row. Deliberately NOT the
    /// flow's <c>firstPageId</c> — whether the guidance is shown depends on the school's data at this
    /// moment, which the static config cannot express.
    /// </summary>
    private const string LateResultsGuidancePageId = "check-late-results";

    private const string SelectionRequired = "Select what issue with the results you need to report";

    [Route("/{windowId:guid}/ResultIssue")]
    public IActionResult Index(Guid windowId)
        // Never pre-selects: the confirmation page's "Report another issue" link lands here, and the
        // AC is that nothing carries over from the previous enquiry.
        => View(new ResultIssueViewModel { WindowId = windowId });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId:guid}/ResultIssue")]
    public async Task<IActionResult> Confirm(Guid windowId, ResultIssueViewModel vm, CancellationToken ct = default)
    {
        // Fail closed on anything that is not the one option this ticket renders, so a forged or
        // sibling-ticket value cannot start a journey with no flow behind it.
        if (vm.IssueType != ResultIssueViewModel.IncorrectGrade)
        {
            ModelState.AddModelError(nameof(ResultIssueViewModel.IssueType), SelectionRequired);
            await analytics.TrackSafeAsync(new ValidationErrorEvent
            {
                ErrorCount = 1,
                ErrorCodes = [ValidationErrorCoding.NoSelection],
                ErrorFields = [nameof(ResultIssueViewModel.IssueType)]
            });
            return View("Index", new ResultIssueViewModel { WindowId = windowId });
        }

        var window = await service.GetCheckingWindowAsync(windowId);

        var config = await flowService.GetConfigAsync(
            Application.CheckYourPupilData.WhatToChange.IncorrectGrade, window.CheckingWindowType);
        if (config is null)
            return RedirectToAction("Index", "CheckYourPupilData", new { windowId });

        // The one gating branch. BA decision 2026-08-17: the guidance INFORMS, it never blocks — the
        // option above is always selectable, and both paths continue into the same journey. The Figma
        // frame that greys the option out ("Incorrect grade option will be available after releasing
        // second late results") was considered and not chosen.
        var available = await lateResults.IsSecondLateResultsAvailableAsync(
            windowId, currentUser.OrganisationLaestab, ct);

        var pageId = available ? config.FirstPageId : LateResultsGuidancePageId;

        // Paired with results_enquiry_submitted to give the start-to-submit funnel. The guidance flag
        // is the measure that answers the question behind the whole interstitial: are we stopping
        // enquiries that did not need raising?
        await analytics.TrackSafeAsync(new ResultsEnquiryStartedEvent
        {
            EnquiryType = ResultIssueViewModel.IncorrectGrade,
            CheckingWindowType = window.CheckingWindowType.ToString(),
            LateResultsGuidanceShown = !available
        });

        // A brand-new RequestState rather than an edit of the old one: the AC requires that starting
        // an enquiry carries nothing over, and listing the fields to clear would silently miss any
        // field added later.
        //
        // When the guidance page is the entry point it is seeded into the history. The flow config's
        // firstPageId is cohort-scope, so without this the journey engine's out-of-sequence guard
        // would bounce the user straight past the guidance to cohort-scope and the "tell me to check
        // that file first" acceptance criterion would never be met. Seeding is also the truthful
        // record: this controller has decided the journey starts there.
        HttpContext.Session.SetRequestState(windowId, new RequestState
        {
            SelectedWhatToChange = Application.CheckYourPupilData.WhatToChange.IncorrectGrade,
            CheckingWindow = window,
            QuestionHistory = available ? [] : [LateResultsGuidancePageId]
        });
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        return RedirectToAction("Page", "Journey", new { windowId, pageId });
    }
}
