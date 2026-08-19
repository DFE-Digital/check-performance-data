using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class TurnaroundCommitmentController(IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/TurnaroundCommitment.cshtml";

    [HttpGet("admin/windows/{id:guid}/turnaround-commitment")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowTurnaroundCommitmentEditItem model = new WindowTurnaroundCommitmentEditItem
        {
            WindowId = window.Id,
            TurnaroundCommitment = window.TurnaroundCommitment,
            PostUrl = Url.Action("Update", "TurnaroundCommitment", new { id = window.Id }),
            CancelUrl = Url.Action("Index", "Summary", new { id = window.Id })
        };

        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/turnaround-commitment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowTurnaroundCommitmentEditItem model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        if (id != model.WindowId)
        {
            return BadRequest();
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }

        window.TurnaroundCommitment = model.TurnaroundCommitment ?? string.Empty;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id = id });
    }
}