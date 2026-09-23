using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Adds a display-only exercise to an existing window (#466). Rules live in IWindowService.</summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class AddExerciseController(IWindowService windowService) : Controller
{
    public const string PageView = "~/Views/WindowAdmin/ExerciseForm.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/add")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }
        return View(PageView, Decorate(new ExerciseFormItem
        {
            StartHour = ExerciseDatesItem.DefaultStartHour,
            EndHour = ExerciseDatesItem.DefaultEndHour,
            SortOrder = WindowExercises.NextDisplayOnlySortOrder(window)
        }, id));
    }

    [HttpPost("admin/windows/{id:guid}/exercises/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, ExerciseFormItem model, CancellationToken cancellationToken)
    {
        if (id != model.WindowId)
        {
            return BadRequest();
        }
        Decorate(model, id);
        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        ExerciseChangeResult result = await windowService.AddExerciseAsync(id,
            new ExerciseDefinition(model.Name, model.TabName, model.SortOrder, model.StartDateTime!.Value, model.EndDateTime!.Value),
            cancellationToken);

        if (!result.Succeeded)
        {
            // The window itself vanished under us (a race with someone else's edit) — nothing on
            // this form can fix that, and there is no sensible field to blame it on.
            if (ExerciseFormErrors.IsMissing(result.Reason))
            {
                return RedirectToAction("Index", "Summary", new { id });
            }
            ModelState.AddModelError(ExerciseFormErrors.FieldFor(result.Reason), result.Reason ?? "The exercise could not be added");
            return View(PageView, model);
        }
        return RedirectToAction("Index", "Summary", new { id });
    }

    private static ExerciseFormItem Decorate(ExerciseFormItem model, Guid id)
    {
        model.WindowId = id;
        model.Heading = "Add exercise";
        model.KindLabel = null;
        model.PostUrl = $"/admin/windows/{id}/exercises/add";
        model.CancelUrl = $"/admin/windows/summary/{id}";
        return model;
    }
}
