using DfE.CheckPerformanceData.Application.AmendmentRequests;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class AmendmentRequestsController(IAmendmentRequestsService service) : Controller
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
}
