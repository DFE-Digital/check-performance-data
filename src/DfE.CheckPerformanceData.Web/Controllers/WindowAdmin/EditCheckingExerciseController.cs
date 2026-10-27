using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class EditCheckingExerciseController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/CreateCheckingExercise.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();

        var model = new CreateCheckingExerciseItem
        {
            WindowId = id,
            Name = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType),
            ExerciseType = exercise.ExerciseType,
            TabName = exercise.TabName,
            TabOrder = exercise.TabOrder,
            SortOrder = exercise.SortOrder,
            IsEnabled = exercise.IsEnabled,
            DisplayOnly = exercise.DisplayOnly,
            VisibleFrom = exercise.VisibleFrom,
            VisibleUntil = exercise.VisibleUntil,
            ReplacesCheckingExerciseId = exercise.ReplacesCheckingExerciseId,
            Dates = new ExerciseDatesItem
            {
                StartDate = exercise.StartDate,
                StartHour = exercise.StartDate.Hour,
                StartMinute = exercise.StartDate.Minute,
                EndDate = exercise.EndDate,
                EndHour = exercise.EndDate.Hour,
                EndMinute = exercise.EndDate.Minute
            }
        };
        Decorate(model, window, exercise);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, Guid exerciseId, CreateCheckingExerciseItem model,
        CancellationToken cancellationToken)
    {
        if (id != model.WindowId) return BadRequest();
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();

        Decorate(model, window, exercise);
        if (!model.TabNameOptional && string.IsNullOrWhiteSpace(model.TabName))
            ModelState.AddModelError(nameof(model.TabName), "Enter a tab name");

        if (model.ReplacesCheckingExerciseId is { } replacementId
            && model.ReplacementOptions.All(e => e.Id != replacementId))
            ModelState.AddModelError(nameof(model.ReplacesCheckingExerciseId),
                "Select another exercise from this window that does not already replace this exercise");

        if (!exercise.UsesExerciseStorage)
        {
            // The exercise type identifies historic output paths. Editing descriptive fields must not
            // move the exercise away from its existing files or collide with another legacy row.
            if (model.ExerciseType != exercise.ExerciseType)
                ModelState.AddModelError(nameof(model.ExerciseType), "The type cannot be changed for this existing exercise");
        }

        if (!ModelState.IsValid) return View(PageView, model);

        var outputChanged = model.ExerciseType != exercise.ExerciseType;
        var updated = new CheckingExerciseDto
        {
            Id = exercise.Id,
            UsesExerciseStorage = exercise.UsesExerciseStorage,
            ExerciseType = model.ExerciseType,
            Name = model.Name!.Trim(),
            TabName = string.IsNullOrWhiteSpace(model.TabName) ? null : model.TabName.Trim(),
            TabOrder = model.TabOrder!.Value,
            SortOrder = model.SortOrder!.Value,
            IsEnabled = model.IsEnabled,
            DisplayOnly = model.DisplayOnly,
            VisibleFrom = model.VisibleFrom,
            VisibleUntil = model.VisibleUntil,
            ReplacesCheckingExerciseId = model.ReplacesCheckingExerciseId,
            StartDate = KeepSeconds(model.Dates.StartDateTime!.Value, exercise.StartDate),
            EndDate = KeepSeconds(model.Dates.EndDateTime!.Value, exercise.EndDate),
            WindowStart = exercise.WindowStart,
            WindowEnd = exercise.WindowEnd,
            Datasets = exercise.Datasets,
            ValidatedAt = outputChanged ? null : exercise.ValidatedAt,
            ValidatedIngressChecksum = exercise.ValidatedIngressChecksum,
            ValidatedSchemaChecksum = exercise.ValidatedSchemaChecksum
        };
        window.Exercises[window.Exercises.IndexOf(exercise)] = updated;
        await windowService.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Decorate(CreateCheckingExerciseItem model, CheckingWindowDto window, CheckingExerciseDto exercise)
    {
        model.DataExercise = exercise;
        model.IsEditing = true;
        model.TabNameOptional = exercise.TabName is null;
        model.WindowTitle = window.Title;
        model.ReplacementOptions = window.Exercises
            .Where(e => !WouldCreateCycle(window, exercise.Id, e.Id))
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToList();
        model.PostUrl = Url.Action("Update", "EditCheckingExercise", new { id = window.Id, exerciseId = exercise.Id });
        model.CancelUrl = Url.Action("Index", "Summary", new { id = window.Id });
    }

    private static bool WouldCreateCycle(CheckingWindowDto window, Guid exerciseId, Guid replacementId)
    {
        var visited = new HashSet<Guid> { exerciseId };
        Guid? current = replacementId;
        while (current is { } id)
        {
            if (!visited.Add(id)) return true;
            current = window.Exercises.SingleOrDefault(e => e.Id == id)?.ReplacesCheckingExerciseId;
        }
        return false;
    }

    private static DateTime KeepSeconds(DateTime posted, DateTime original) =>
        posted == original.AddTicks(-(original.Ticks % TimeSpan.TicksPerMinute)) ? original : posted;
}
