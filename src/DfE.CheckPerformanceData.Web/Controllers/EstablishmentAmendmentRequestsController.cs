using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class EstablishmentAmendmentRequestsController: Controller
{
    [Route("/establishment-amendment-requests")]
    public async Task<IActionResult> Index()
    {
        var viewModel = new EstablishmentAmendmentRequestsViewModel
        {
            ActiveWindows = new List<ActiveWindow>()
            {
                new ActiveWindow() { WindowTitle = "dave", DeadlineText = "test"}
            },
            Rows = new List<AmendmentItem>
            {
                new AmendmentItem { PupilName = "Row 1", ReferenceNumber = "Seedx001", RequestType = "Remove", Status = "Submitted", WindowName = "Window 1", DateSubmitted = "2023-01-01", WindowId = "1", WindowIsOpen = false},
                new AmendmentItem { PupilName = "Row 2", ReferenceNumber = "Seedx002", RequestType = "Delete", Status = "Withdraw", WindowName = "Window 1", DateSubmitted = "2023-01-01", WindowId = "2", WindowIsOpen = true}

            }
        };
        return View(viewModel);
    }
}