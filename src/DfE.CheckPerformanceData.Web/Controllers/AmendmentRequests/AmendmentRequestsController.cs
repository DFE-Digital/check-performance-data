using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsController(
    IAmendmentRequestsService service,
    IRequestService requestService,
    IEditAdviceService adviceService,
    IAnalyticsService analytics,
    IBulkSubmissionService bulkService,
    ICheckYourPupilDataService checkYourPupilDataService) : Controller
{
    private const string BulkSubmittedRefsKey = "BulkSubmittedRefs";

    private async Task<AmendmentRequestsViewModel> BuildIndexViewModelAsync(Guid windowId)
    {
        var result = await service.GetAmendmentRequestsAsync(windowId);
        var deadline = result.WindowEndDate;
        return new AmendmentRequestsViewModel
        {
            WindowId = windowId,
            WindowTitle = result.WindowTitle,
            DeadlineText = $"{deadline.ToString("htt").ToLower()} on {deadline:dddd d MMMM yyyy}",
            Rows = result.Rows.Select(r => new AmendmentRequestRowViewModel
            {
                PupilName = r.PupilName,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
            }).ToList(),
            SubmittedRows = result.SubmittedRows.Select(r => new SubmittedRequestRowViewModel
            {
                PupilName = r.PupilName,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted
            }).ToList()
        };
    }

    [Route("/{windowId}/AmendmentRequests")]
    public async Task<IActionResult> Index(Guid windowId) =>
        View(await BuildIndexViewModelAsync(windowId));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/bulk")]
    public async Task<IActionResult> BulkReview(Guid windowId, string[] selectedReferences)
    {
        if (selectedReferences is null || selectedReferences.Length == 0)
        {
            ModelState.AddModelError("selectedReferences", "Select the pupil(s) you want to submit");
            return View("Index", await BuildIndexViewModelAsync(windowId));
        }

        HttpContext.Session.SetBulkSelection(windowId, selectedReferences);
        return RedirectToAction(nameof(BulkReviewPage), new { windowId });
    }

    [HttpGet]
    [Route("/{windowId}/AmendmentRequests/bulk")]
    public async Task<IActionResult> BulkReviewPage(Guid windowId)
    {
        var selected = HttpContext.Session.GetBulkSelection(windowId);
        if (selected.Count == 0)
            return RedirectToAction(nameof(Index), new { windowId });

        var review = await bulkService.BuildReviewAsync(windowId, selected);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        return View("BulkReview", new BulkReviewViewModel
        {
            WindowId = windowId,
            WindowTitle = window.Title,
            Submittable = review.Submittable.Select(ToItemVm).ToList(),
            Duplicates = review.Duplicates.Select(ToItemVm).ToList()
        });
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
        var deadline = window.EndDate;

        return View("BulkConfirmation", new BulkConfirmationViewModel
        {
            WindowId = windowId,
            ReferenceNumbers = references,
            WindowCloseLabel = $"{deadline.ToString("htt").ToLower()} on {deadline:dddd d MMMM yyyy}"
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

        HttpContext.Session.SetRequestState(windowId, journey);

        await analytics.TrackSafeAsync(new DraftResumedEvent
        {
            ReferenceNumber = referenceNumber,
            WhatToChange = journey.SelectedWhatToChange?.ToString() ?? "",
            CheckingWindowType = journey.CheckingWindow?.CheckingWindowType.ToString() ?? "",
        });

        var advice = await adviceService.BuildAsync(windowId, referenceNumber, journey);
        if (advice is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        // These mirror the [Route] templates on Index / BulkReviewPage (kept as literals to avoid
        // IUrlHelper in unit tests). If you change those routes, update these too.
        var backUrl = fromBulk
            ? $"/{windowId}/AmendmentRequests/bulk"
            : $"/{windowId}/AmendmentRequests";

        return View("EditAdvice", new EditAdviceViewModel
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            PupilName = advice.PupilName,
            AdviceText = advice.AdviceText,
            EvidenceMessages = advice.EvidenceMessages,
            ReasonForRemoval = advice.ReasonForRemoval,
            BackUrl = backUrl
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
