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

        var model = new WindowTurnaroundCommitmentEditItem
        {
            WindowId = window.Id,
            TurnaroundCommitment = window.TurnaroundCommitment
        };
        Decorate(model, id);

        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/turnaround-commitment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowTurnaroundCommitmentEditItem model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            // The urls are not posted back, so a redisplayed page has to be given them again —
            // otherwise its form action and Cancel link are empty (AB#298317 review; the same fix
            // NextOpportunityController shipped with).
            Decorate(model, id);
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

        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Decorate(WindowTurnaroundCommitmentEditItem model, Guid id)
    {
        model.PostUrl = Url.Action("Update", "TurnaroundCommitment", new { id });
        model.CancelUrl = Url.Action("Index", "Summary", new { id });
    }
}
