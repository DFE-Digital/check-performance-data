using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class EstablishmentAmendmentRequestsController(IUrnAmendmentRequestsService service): Controller
{
    [Route("/establishment-amendment-requests")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        UrnAmendmentRequestsResult result = await service.GetAllSubmittedAmendmentRequestsAsync(cancellationToken);
        HashSet<Guid> openWindowIds = result.OpenWindows.Select(w => w.WindowId).ToHashSet();
        EstablishmentAmendmentRequestsViewModel viewModel = new EstablishmentAmendmentRequestsViewModel
        {
            ActiveWindows = result.OpenWindows.Select(w => new ActiveWindow
            {
                WindowId = w.WindowId.ToString(),
                WindowTitle = w.WindowName,
                DeadlineText = w.WindowEndDate.ToString("dd-MM-yyyy")
            }).ToList(),
            Rows = result.SubmittedRows.Select(r => new AmendmentItem
            {
                PupilName = r.PupilName,
                ReferenceNumber = r.ReferenceNumber,
                RequestType = r.RequestType.ToString(),
                Status = r.Status.ToString(),
                DateSubmitted = r.Submitted.ToString("dd-MM-yyyy"),
                WindowName = r.WindowName,
                WindowId = r.WindowId.ToString(),
                WindowIsOpen = openWindowIds.Contains(r.WindowId)
            }).ToList()

        };
        return View(viewModel);
    }
}