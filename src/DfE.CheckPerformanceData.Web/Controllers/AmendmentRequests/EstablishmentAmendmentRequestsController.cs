using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class EstablishmentAmendmentRequestsController(
    IUrnAmendmentRequestsService urnAmendmentRequestsService, 
    ISubmittedRequestService submittedRequestService): Controller
{
    [Route("/establishment-amendment-requests")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        UrnAmendmentRequestsResult result = await urnAmendmentRequestsService.GetAllSubmittedAmendmentRequestsAsync(cancellationToken);
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
                Status = r.Status,
                DateSubmitted = r.Submitted.ToString("dd-MM-yyyy"),
                WindowName = r.WindowName,
                WindowId = r.WindowId.ToString(),
                WindowIsOpen = openWindowIds.Contains(r.WindowId)
            }).ToList()

        };
        return View(viewModel);
    }
    
    [Route("/establishment-amendment-requests/{windowId:guid}/view/{referenceNumber}")]
    public async Task<IActionResult> View(Guid windowId, string referenceNumber, CancellationToken cancellationToken)
    {
        SubmittedRequestView? request = await submittedRequestService.GetAsync(windowId, referenceNumber);
        if (request == null)
        {
            return NotFound();
        }
        
        return View("~/Views/AmendmentRequests/UrnAmendmentView.cshtml", new SubmittedRequestViewModel
        {
            WindowId = windowId,
            WhatToChange = request.WhatToChange,
            Status = request.Status,
            ConfirmingDelete = false,
            PupilName = request.PupilName,
            FirstRecordDisplay = request.FirstRecordDisplay,
            SecondRecordDisplay = request.SecondRecordDisplay,
            Rows = request.Rows.Select(r => new SubmittedRequestRow
            {
                Title = r.Title,
                DisplayValue = r.DisplayValue
            }).ToList(),
            Files = request.Files.Select(f => new SubmittedRequestFile
            {
                OriginalFileName = f.OriginalFileName,
                StoredFileName = f.StoredFileName,
                FileSizeBytes = f.FileSizeBytes
            }).ToList(),
            ReferenceNumber = request.ReferenceNumber,
            SubmittedByEmail = request.SubmittedByEmail,
            SubmittedAt = request.SubmittedAt,
            WithdrawnByEmail = request.WithdrawnByEmail,
            WithdrawnAtText = LondonTime.ToSubmittedAtText(request.WithdrawnAt)
        });
    }
}