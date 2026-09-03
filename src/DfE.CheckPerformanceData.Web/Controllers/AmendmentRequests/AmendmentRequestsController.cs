using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsController(
    IAmendmentRequestsService service,
    IRequestService requestService,
    IEditAdviceService adviceService,
    IAnalyticsService analytics,
    IBulkSubmissionService bulkService,
    ICheckYourPupilDataService checkYourPupilDataService,
    IQuestionFlowService flowService,
    ICheckingExerciseService checkingExercises,
    IJourneyViewModelBuilder viewModelBuilder) : Controller
{
    private const string BulkSubmittedRefsKey = "BulkSubmittedRefs";

    private async Task<AmendmentRequestsViewModel> BuildIndexViewModelAsync(Guid windowId, string? issueSearch)
    {
        var result = await service.GetAmendmentRequestsAsync(windowId, issueSearch);
        // Re-check the boxes that were selected before going into the bulk review (kept in session).
        var selected = HttpContext.Session.GetBulkSelection(windowId).ToHashSet(StringComparer.Ordinal);
        return new AmendmentRequestsViewModel
        {
            WindowId = windowId,
            WindowTitle = result.WindowTitle,
            LearnerNoun = result.LearnerNoun,
            Deadlines = result.Deadlines.Select(d => new ExerciseDeadlineViewModel
            {
                Exercise = d.Exercise,
                EndDate = d.EndDate,
                IsOpen = d.IsOpen,
                LearnerNoun = result.LearnerNoun
            }).ToList(),
            Rows = result.Rows.Select(r => new AmendmentRequestRowViewModel
            {
                PupilName = r.PupilName,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber,
                IsSelected = selected.Contains(r.ReferenceNumber)
            }).ToList(),
            SubmittedRows = result.SubmittedRows.Select(r => new SubmittedRequestRowViewModel
            {
                PupilName = r.PupilName,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted
            }).ToList(),
            IssueRows = result.IssueRows.Select(r => new IssueRowViewModel
            {
                PupilName = r.PupilName,
                Submitted = r.Submitted,
                CypmdId = r.CypmdId,
                TypeLabel = r.TypeLabel,
                QualificationText = r.QualificationText
            }).ToList(),
            HasAnyIssues = result.HasAnyIssues,
            IssueSearch = issueSearch
        };
    }

    [Route("/{windowId}/AmendmentRequests")]
    public async Task<IActionResult> Index(Guid windowId, string? issueSearch = null) =>
        View(await BuildIndexViewModelAsync(windowId, issueSearch));

    // Renders each submittable request as a full journey-style summary (no change links) rather
    // than a one-line row, with duplicates shown in a compact warning table.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/bulk-detailed")]
    public async Task<IActionResult> BulkReviewDetailed(Guid windowId, string[] selectedReferences)
    {
        if (selectedReferences is null || selectedReferences.Length == 0)
        {
            ModelState.AddModelError("selectedReferences", "Select the amendment request(s) you would like to submit");
            return View("Index", await BuildIndexViewModelAsync(windowId, null));
        }

        HttpContext.Session.SetBulkSelection(windowId, selectedReferences);
        return RedirectToAction(nameof(BulkReviewDetailedPage), new { windowId });
    }

    [HttpGet]
    [Route("/{windowId}/AmendmentRequests/bulk-detailed")]
    public async Task<IActionResult> BulkReviewDetailedPage(Guid windowId)
    {
        // Returning to the batch review clears any per-request edit context set by Edit.
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        var selected = HttpContext.Session.GetBulkSelection(windowId);
        if (selected.Count == 0)
            return RedirectToAction(nameof(Index), new { windowId });

        var review = await bulkService.BuildReviewAsync(windowId, selected);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        var submittable = new List<BulkDetailedItemViewModel>();
        foreach (var item in review.Submittable)
        {
            var summary = await BuildRequestSummaryAsync(windowId, item.ReferenceNumber);
            if (summary is not null)
                submittable.Add(new BulkDetailedItemViewModel
                {
                    ReferenceNumber = item.ReferenceNumber,
                    Summary = summary
                });
        }

        return View("BulkReviewDetailed", new BulkReviewDetailedViewModel
        {
            WindowId = windowId,
            WindowTitle = window.Title,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType),
            Duplicates = review.Duplicates.Select(ToItemVm).ToList(),
            Submittable = submittable
        });
    }

    // Resumes a draft and projects its full journey summary. Returns null when the draft can no
    // longer be resolved (missing/malformed) so the caller can skip it rather than fail the page.
    private async Task<SummaryViewModel?> BuildRequestSummaryAsync(Guid windowId, string referenceNumber)
    {
        var journey = await requestService.ResumeDraftAsync(windowId, referenceNumber);
        if (journey?.SelectedWhatToChange is null || journey.CheckingWindow is null
            || journey.QuestionHistory.Count == 0)
            return null;

        var config = await flowService.GetConfigAsync(
            journey.SelectedWhatToChange.Value, journey.CheckingWindow.CheckingWindowType);
        return config is null ? null : viewModelBuilder.BuildSummaryVm(windowId, journey, config);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/bulk/submit")]
    public async Task<IActionResult> BulkSubmit(Guid windowId, string[] references)
    {
        var result = await bulkService.SubmitAsync(windowId, references ?? []);
        HttpContext.Session.ClearBulkSelection(windowId);
        // Reference numbers are server-generated and never contain commas, so join/split is safe.
        TempData[BulkSubmittedRefsKey] = string.Join(",", result.Submitted);
        return RedirectToAction(nameof(BulkConfirmation), new { windowId });
    }

    [Route("/{windowId}/AmendmentRequests/bulk/confirmation")]
    public async Task<IActionResult> BulkConfirmation(Guid windowId)
    {
        var raw = TempData[BulkSubmittedRefsKey] as string;
        var references = string.IsNullOrEmpty(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (references.Length == 0)
            return RedirectToAction(nameof(Index), new { windowId });

        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        // #320: the pupil-data exercise's end, never the outer window's. The banner below invites
        // the school to request another amendment, and that journey shuts when pupil data shuts —
        // on a 16-19 window the outer end is the results-enquiry close, months later.
        var deadline = checkingExercises.EndDateFor(window.Exercises, CheckingExerciseType.PupilData);

        return View("BulkConfirmation", new BulkConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumbers = references,
            WindowCloseLabel = deadline is null
                ? null
                : $"{deadline.Value.ToString("htt").ToLower()} on {deadline.Value:dddd d MMMM yyyy}"
        });
    }

    private static BulkReviewItemViewModel ToItemVm(BulkReviewItem i) => new()
    {
        ReferenceNumber = i.ReferenceNumber,
        PupilName = i.PupilName,
        RequestTypeDescription = i.RequestTypeDescription,
        DuplicateReason = i.DuplicateReason
    };

    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/edit")]
    public async Task<IActionResult> Edit(Guid windowId, string referenceNumber, bool fromBulk = false)
    {
        var journey = await requestService.ResumeDraftAsync(windowId, referenceNumber);
        if (journey is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        // #318, product decision 2026-08-20: a draft saved while the exercise was open cannot be
        // reopened once it has closed. Blocking the resume rather than the submit keeps the gate in
        // one place and stops a user editing a request that could never be sent. The draft itself
        // stays listed and readable on Amendment Requests — closed removes actions, never content.
        if (journey.SelectedWhatToChange is { } draftChange && journey.CheckingWindow is not null)
        {
            var draftExercise = WhatToChangeCheckingExerciseMap.CheckingExerciseFor(draftChange);
            if (!checkingExercises.IsOpen(journey.CheckingWindow.Exercises, draftExercise))
                return this.RedirectExerciseClosed(windowId, draftExercise, journey.LearnerNoun);
        }

        HttpContext.Session.SetRequestState(windowId, journey);

        await analytics.TrackSafeAsync(new DraftResumedEvent
        {
            ReferenceNumber = referenceNumber,
            WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
            CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
        });

        // From the bulk review page, skip the edit-advice interstitial and go straight to the
        // journey summary (session state is now primed for it). The bulk-edit flag tells the
        // summary to link back to the review page and hide its own submit/save actions.
        if (fromBulk)
        {
            HttpContext.Session.SetBulkEditMode(windowId);
            HttpContext.Session.ClearSingleEditMode(windowId);
            return RedirectToAction("Summary", "Journey", new { windowId });
        }

        // A normal edit clears any stale bulk-edit context so the summary shows its actions, and
        // marks single-edit mode so the summary links back to the Amendment Requests page.
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.SetSingleEditMode(windowId);

        var advice = await adviceService.BuildAsync(windowId, referenceNumber, journey);
        if (advice is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        // Mirrors the [Route] template on Index (kept as a literal to avoid IUrlHelper in unit
        // tests). If you change that route, update this too. Reached only for non-bulk edits —
        // the bulk path returns to the summary above.
        var backUrl = $"/{windowId}/AmendmentRequests";

        return View("EditAdvice", new EditAdviceViewModel
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            PupilName = advice.PupilName,
            AdviceText = advice.AdviceText,
            EvidenceMessages = advice.EvidenceMessages,
            ReasonForRemoval = advice.ReasonForRemoval,
            BackUrl = backUrl,
            SavedByEmail = advice.SubmittedByEmail,
            SavedAt = advice.SubmittedAt
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/continue")]
    public async Task<IActionResult> Continue(Guid windowId, string referenceNumber)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        var advice = await adviceService.BuildAsync(windowId, referenceNumber, journey);
        if (advice is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        return advice.ContinueTarget switch
        {
            ContinueToPage page => RedirectToAction("Page", "Journey", new { windowId, pageId = page.PageId }),
            _ => RedirectToAction("Summary", "Journey", new { windowId })
        };
    }
}
