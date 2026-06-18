using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.FileStorage;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class SubmittedRequestController(
    ISubmittedRequestService service,
    IFileStorageService fileStorageService) : Controller
{
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/view")]
    public async Task<IActionResult> View(Guid windowId, string referenceNumber)
    {
        var request = await service.GetAsync(windowId, referenceNumber);
        if (request is null)
            return RedirectToAction("Index", "AmendmentRequests", new { windowId });

        return View(new SubmittedRequestViewModel
        {
            WindowId = windowId,
            WhatToChange = request.WhatToChange,
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
            SubmittedAt = request.SubmittedAt
        });
    }

    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/view-confirmation")]
    public async Task<IActionResult> ViewConfirmation(Guid windowId, string referenceNumber)
    {
        var request = await service.GetConfirmDataCorrectAsync(windowId, referenceNumber);
        if (request is null)
            return RedirectToAction("Index", "AmendmentRequests", new { windowId });

        return View(new ConfirmDataCorrectViewModel
        {
            WindowId = windowId,
            SubmittedByEmail = request.SubmittedByEmail,
            SubmittedAt = request.SubmittedAt,
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

    // Placeholder: deleting a submitted request is not wired up yet. Returns to the
    // summary so the button is harmless for now.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/{windowId}/AmendmentRequests/{referenceNumber}/delete")]
    public IActionResult Delete(Guid windowId, string referenceNumber) =>
        RedirectToAction(nameof(View), new { windowId, referenceNumber });
}
