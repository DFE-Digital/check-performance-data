using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Renames, reorders or re-dates one exercise (#466). Replaces the per-kind dates page.</summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class EditExerciseController(IWindowService windowService) : Controller
{
    private const string PageView = AddExerciseController.PageView;

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    public async Task<IActionResult> Index(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exerciseId);
        if (target is null)
        {
            return NotFound();
        }
        return View(PageView, Decorate(new ExerciseFormItem
        {
            Name = target.Name,
            TabName = target.TabName,
            SortOrder = target.SortOrder,
            StartDate = target.StartDate.Date,
            StartHour = target.StartDate.Hour,
            StartMinute = target.StartDate.Minute,
            EndDate = target.EndDate.Date,
            EndHour = target.EndDate.Hour,
            EndMinute = target.EndDate.Minute
        }, id, exerciseId, target));
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, Guid exerciseId, ExerciseFormItem model, CancellationToken cancellationToken)
    {
        if (id != model.WindowId)
        {
            return BadRequest();
        }
        if (!ModelState.IsValid)
        {
            return await Redisplay(id, exerciseId, model, cancellationToken);
        }

        ExerciseChangeResult result = await windowService.UpdateExerciseAsync(id, exerciseId,
            new ExerciseDefinition(model.Name, model.TabName, model.SortOrder, model.StartDateTime!.Value, model.EndDateTime!.Value),
            cancellationToken);

        if (!result.Succeeded)
        {
            // The exercise (or window) vanished under us — Redisplay's own lookup would 404 anyway,
            // discarding whatever we put in ModelState, so go straight to Summary instead.
            if (ExerciseFormErrors.IsMissing(result.Reason))
            {
                return RedirectToAction("Index", "Summary", new { id });
            }
            ModelState.AddModelError(ExerciseFormErrors.FieldFor(result.Reason), result.Reason ?? "The exercise could not be saved");
            return await Redisplay(id, exerciseId, model, cancellationToken);
        }
        return RedirectToAction("Index", "Summary", new { id });
    }

    // The heading and kind label are not posted back, so a redisplayed page reads the row again.
    private async Task<IActionResult> Redisplay(Guid id, Guid exerciseId, ExerciseFormItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exerciseId);
        if (target is null)
        {
            return NotFound();
        }
        return View(PageView, Decorate(model, id, exerciseId, target));
    }

    private static ExerciseFormItem Decorate(ExerciseFormItem model, Guid id, Guid exerciseId, CheckingExerciseDto target)
    {
        model.WindowId = id;
        model.Heading = $"Edit {target.Name}";
        model.KindLabel = target.ExerciseType is { } kind
            ? CheckingExerciseNames.NameFor(kind)
            : CheckingExerciseNames.DisplayOnlyLabel;
        model.PostUrl = $"/admin/windows/{id}/exercises/{exerciseId}/edit";
        model.CancelUrl = $"/admin/windows/summary/{id}";
        return model;
    }
}
