using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class CreateCheckingExerciseController(IWindowService windowService, TimeProvider timeProvider) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/CreateCheckingExercise.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/new")]
    public async Task<IActionResult> New(Guid id, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null) return NotFound();

        var model = new CreateCheckingExerciseItem { WindowId = id };
        Decorate(model, window);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, CreateCheckingExerciseItem model, CancellationToken cancellationToken)
    {
        if (id != model.WindowId) return BadRequest();

        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null) return NotFound();

        Decorate(model, window);
        if (string.IsNullOrWhiteSpace(model.TabName))
            ModelState.AddModelError(nameof(model.TabName), "Enter a tab name");

        if (model.ReplacesCheckingExerciseId is { } replacementId
            && window.Exercises.All(e => e.Id != replacementId))
            ModelState.AddModelError(nameof(model.ReplacesCheckingExerciseId), "Select an exercise from this window");

        if (ModelState.IsValid && model.Dates.StartDateTime < timeProvider.GetLocalNow().Date)
            ModelState.AddModelError("Dates.StartDate", "Start date cannot occur in the past");

        if (!ModelState.IsValid) return View(PageView, model);

        // Empty IDs are matched by type by the legacy wizard. Assign an identity here so that
        // adding another release of the same type cannot overwrite an existing exercise.
        window.Exercises.Add(new CheckingExerciseDto
        {
            Id = Guid.NewGuid(),
            ExerciseType = model.ExerciseType!.Value,
            Name = model.Name!.Trim(),
            TabName = model.TabName!.Trim(),
            TabOrder = model.TabOrder!.Value,
            SortOrder = model.SortOrder!.Value,
            IsEnabled = model.IsEnabled,
            VisibleFrom = model.VisibleFrom,
            VisibleUntil = model.VisibleUntil,
            ReplacesCheckingExerciseId = model.ReplacesCheckingExerciseId,
            StartDate = model.Dates.StartDateTime!.Value,
            EndDate = model.Dates.EndDateTime!.Value,
            Datasets = WindowDatasets.DefaultsFor(window.CheckingWindowType, model.ExerciseType.Value).ToList()
        });
        await windowService.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Decorate(CreateCheckingExerciseItem model, CheckingWindowDto window)
    {
        model.WindowTitle = window.Title;
        model.ReplacementOptions = window.Exercises.OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToList();
        model.PostUrl = Url.Action("Submit", "CreateCheckingExercise", new { id = window.Id });
        model.CancelUrl = Url.Action("Index", "Summary", new { id = window.Id });
    }
}
