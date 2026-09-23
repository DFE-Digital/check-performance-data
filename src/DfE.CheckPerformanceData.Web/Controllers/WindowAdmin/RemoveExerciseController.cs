using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Confirm-then-remove one exercise (#466), same shape as CloseExerciseController.</summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class RemoveExerciseController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/RemoveExercise.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/remove")]
    public async Task<IActionResult> Confirm(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        return await ConfirmView(id, exerciseId, refusal: null, cancellationToken);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        ExerciseChangeResult result = await windowService.RemoveExerciseAsync(id, exerciseId, cancellationToken);
        if (!result.Succeeded)
        {
            // A double-submit (the confirm button pressed twice) removes the exercise on the first
            // POST and is refused "Exercise not found" on the second — ConfirmView's own lookup
            // would then find nothing and answer a successful removal with a 404. Treat it the same
            // as the first, successful removal rather than distinguishing the race.
            if (ExerciseFormErrors.IsMissing(result.Reason))
            {
                return RedirectToAction("Index", "Summary", new { id });
            }
            return await ConfirmView(id, exerciseId, result.Reason, cancellationToken);
        }
        return RedirectToAction("Index", "Summary", new { id });
    }

    private async Task<IActionResult> ConfirmView(Guid id, Guid exerciseId, string? refusal, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exerciseId);
        if (window is null || target is null)
        {
            return NotFound();
        }
        if (refusal is not null)
        {
            ViewData["HasError"] = true;
        }
        return View(PageView, new RemoveExerciseViewModel
        {
            WindowId = id,
            ExerciseId = exerciseId,
            WindowTitle = window.Title,
            ExerciseLabel = target.Name,
            FileCount = target.Datasets.Sum(d =>
                (string.IsNullOrWhiteSpace(d.IngressFile) ? 0 : 1) + (string.IsNullOrWhiteSpace(d.SchemaFile) ? 0 : 1)),
            Refusal = refusal
        });
    }
}
