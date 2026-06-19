using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsController(
    IAmendmentRequestsService service,
    IRequestService requestService,
    IEditAdviceService adviceService) : Controller
{
    [Route("/{windowId}/AmendmentRequests")]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var result = await service.GetAmendmentRequestsAsync(windowId);
        var deadline = result.WindowEndDate;
        return View(new AmendmentRequestsViewModel
        {
            WindowId = windowId,
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
        });
    }

    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/edit")]
    public async Task<IActionResult> Edit(Guid windowId, string referenceNumber)
    {
        var journey = await requestService.ResumeDraftAsync(windowId, referenceNumber);
        if (journey is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        HttpContext.Session.SetRequestState(windowId, journey);

        var advice = await adviceService.BuildAsync(windowId, referenceNumber, journey);
        if (advice is null)
            return RedirectToAction(nameof(Index), "AmendmentRequests", new { windowId });

        return View("EditAdvice", new EditAdviceViewModel
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            PupilName = advice.PupilName,
            AdviceText = advice.AdviceText,
            EvidenceMessages = advice.EvidenceMessages,
            ReasonForRemoval = advice.ReasonForRemoval
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
