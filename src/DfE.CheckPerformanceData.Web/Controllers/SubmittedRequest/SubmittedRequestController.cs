using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class SubmittedRequestController(
    ISubmittedRequestService service,
    IRequestService requestService,
    IFileStorageService fileStorageService,
    IAnalyticsService analytics) : Controller
{
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/view")]
    public Task<IActionResult> View(Guid windowId, string referenceNumber) =>
        RenderAmendment(windowId, referenceNumber, confirmingDelete: false);

    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/view-confirmation")]
    public Task<IActionResult> ViewConfirmation(Guid windowId, string referenceNumber) =>
        RenderConfirmation(windowId, referenceNumber, confirmingDelete: false);

    // "Are you sure?" page for an amendment request — a confirm-mode rendering of View.
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/delete")]
    public Task<IActionResult> ConfirmDelete(Guid windowId, string referenceNumber, bool allEst = false) =>
        RenderAmendment(windowId, referenceNumber, confirmingDelete: true, allEst);

    // "Are you sure?" page for a confirm-data request — a confirm-mode rendering of ViewConfirmation.
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/delete-confirmation")]
    public Task<IActionResult> ConfirmDeleteConfirmation(Guid windowId, string referenceNumber) =>
        RenderConfirmation(windowId, referenceNumber, confirmingDelete: true);

    private async Task<IActionResult> RenderAmendment(Guid windowId, string referenceNumber, bool confirmingDelete, bool allEst = false)
    {
        var request = await service.GetAsync(windowId, referenceNumber);
        if (request is null)
            return RedirectToAction("Index", "AmendmentRequests", new { windowId });

        return View("View", new SubmittedRequestViewModel
        {
            WindowId = windowId,
            WhatToChange = request.WhatToChange,
            Status = request.Status,
            ConfirmingDelete = confirmingDelete,
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
            WithdrawnAtText = LondonTime.ToSubmittedAtText(request.WithdrawnAt),
            AllEst = allEst
        });
    }

    private async Task<IActionResult> RenderConfirmation(Guid windowId, string referenceNumber, bool confirmingDelete)
    {
        var request = await service.GetConfirmDataCorrectAsync(windowId, referenceNumber);
        if (request is null)
            return RedirectToAction("Index", "AmendmentRequests", new { windowId });

        return View("ViewConfirmation", new ConfirmDataCorrectViewModel
        {
            WindowId = windowId,
            Status = request.Status,
            ConfirmingDelete = confirmingDelete,
            SubmittedByEmail = request.SubmittedByEmail,
            SubmittedAt = request.SubmittedAt,
            WithdrawnByEmail = request.WithdrawnByEmail,
            WithdrawnAtText = LondonTime.ToSubmittedAtText(request.WithdrawnAt),
            ReferenceNumber = request.ReferenceNumber
        });
    }

    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/evidence/{storedFileName}")]
    public async Task<IActionResult> DownloadEvidence(Guid windowId, string referenceNumber, string storedFileName)
    {
        if (!Guid.TryParse(storedFileName, out _)) return NotFound();

        var request = await service.GetAsync(windowId, referenceNumber);
        var file = request?.Files.FirstOrDefault(f => f.StoredFileName == storedFileName);
        if (file is null) return NotFound();

        var bytes = await fileStorageService.GetAsync(windowId, storedFileName);
        if (bytes is null) return NotFound();

        return File(bytes, "application/pdf", file.OriginalFileName);
    }

    // Deletes the request and returns to the amendment requests summary. Drafts are
    // hard-deleted (with a confirmation banner); submitted requests are withdrawn and
    // shown as "Withdrawn".
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/delete")]
    public async Task<IActionResult> Delete(Guid windowId, string referenceNumber, bool allEst = false)
    {
        var result = await requestService.DeleteAsync(windowId, referenceNumber);

        // Both amendment requests and data-correct confirmations post to this action;
        // the deleted row's type picks the analytics event. No row found → no event.
        if (result.RequestType is not null)
        {
            AnalyticsEvent deletedEvent = result.RequestType == RequestType.ConfirmCorrect
                ? new ConfirmationDeletedEvent { ReferenceNumber = referenceNumber }
                : new AmendmentRequestDeletedEvent
                {
                    ReferenceNumber = referenceNumber,
                    WasHardDeleted = result.WasHardDeleted,
                };
            await analytics.TrackSafeAsync(deletedEvent);
        }

        TempData["DeletedMessage"] = result.WasHardDeleted
            ? $"{result.PupilName} has been removed from your saved request"
            : $"{result.PupilName}(reference number - {referenceNumber}) has been removed from your submitted request.";

        if (allEst)
        {
            return RedirectToAction("Index", "EstablishmentAmendmentRequests");
        }
        
        return RedirectToAction("Index", "AmendmentRequests", new { windowId });
    }
}
