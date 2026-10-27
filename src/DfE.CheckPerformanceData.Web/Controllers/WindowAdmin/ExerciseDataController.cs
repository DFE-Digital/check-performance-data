using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class ExerciseDataController(IWindowService windows) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/AddExerciseData.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/data/new")]
    public async Task<IActionResult> New(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windows.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();

        var model = new AddExerciseDataItem { WindowId = id };
        Decorate(model, window, exercise);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/data/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, Guid exerciseId, AddExerciseDataItem model, CancellationToken cancellationToken)
    {
        if (model.WindowId != id) return BadRequest();
        var window = await windows.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();
        Decorate(model, window, exercise);

        if (exercise.Datasets.Any(d => string.Equals(d.Name, model.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(model.Name), "This exercise already has a data file with this name");
        if (!string.IsNullOrEmpty(model.SourceFile) && !model.SourceOptions.Contains(model.SourceFile))
            ModelState.AddModelError(nameof(model.SourceFile), "Select a source from the list");
        if (!ModelState.IsValid) return View(PageView, model);

        exercise.Datasets.Add(new CheckingWindowDatasetDto
        {
            Name = model.Name!.Trim(),
            Required = model.Required,
            Included = model.Inclusion switch { "included" => true, "excluded" => false, _ => null },
            SourceFile = string.IsNullOrEmpty(model.SourceFile) ? null : model.SourceFile,
            SortOrder = exercise.Datasets.Count == 0 ? 0 : exercise.Datasets.Max(d => d.SortOrder) + 1
        });
        // Adding a required input changes completeness even before it has received a file.
        exercise.ValidatedAt = null;
        await windows.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Edit", "EditCheckingExercise", new { id, exerciseId }, "data-files");
    }

    private void Decorate(AddExerciseDataItem model, CheckingWindowDto window, CheckingExerciseDto exercise)
    {
        model.ExerciseName = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType);
        model.SourceOptions = WindowDatasets.DefaultsFor(window.CheckingWindowType, CheckingExerciseType.ResultsEnquiry)
            .Select(d => d.SourceFile).OfType<string>().Distinct().ToList();
        model.PostUrl = Url.Action("Submit", "ExerciseData", new { id = window.Id, exerciseId = exercise.Id });
        model.CancelUrl = Url.Action("Edit", "EditCheckingExercise", new { id = window.Id, exerciseId = exercise.Id });
    }
}
