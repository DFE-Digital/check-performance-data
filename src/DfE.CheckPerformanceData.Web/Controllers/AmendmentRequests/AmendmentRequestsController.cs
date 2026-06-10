using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsController(
    IAmendmentRequestsService service,
    IRequestService requestService) : Controller
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
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
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
        return RedirectToAction("Summary", "Journey", new { windowId });
    }
}
