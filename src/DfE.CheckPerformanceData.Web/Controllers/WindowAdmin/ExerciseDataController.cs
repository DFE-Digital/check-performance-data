using DfE.CheckPerformanceData.Application.ResultsEnquiry;
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
        if (model.AsksSource && !string.IsNullOrEmpty(model.SourceFile) && model.SourceOptions.All(o => o.Tag != model.SourceFile))
            ModelState.AddModelError(nameof(model.SourceFile), "Select a source from the list");
        if (!ModelState.IsValid) return View(PageView, model);

        var sourceFile = model.AsksSource && !string.IsNullOrEmpty(model.SourceFile) ? model.SourceFile : null;
        exercise.Datasets.Add(new CheckingWindowDatasetDto
        {
            Name = model.Name!.Trim(),
            Required = model.Required,
            // Each is asked only on the exercise it applies to, so a value posted anywhere else
            // is ignored.
            Included = model.AsksInclusion
                ? model.Inclusion switch { "included" => true, "excluded" => false, _ => null }
                : null,
            SourceFile = sourceFile,
            // On pupil data checking every file is merged into the pupils data the journey reads.
            // On a results enquiry a file with a results source is supplier results and feeds the
            // journey; one with no source is display only. A data share has no journey.
            FeedsJourney = WindowDatasets.AddedSlotFeedsJourney(exercise.ExerciseType, sourceFile),
            SortOrder = exercise.Datasets.Count == 0 ? 0 : exercise.Datasets.Max(d => d.SortOrder) + 1
        });
        // Adding a required input changes completeness even before it has received a file.
        exercise.ValidatedAt = null;
        await windows.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Edit", "EditCheckingExercise", new { id, exerciseId }, ExerciseLinks.DataTab);
    }

    private const string RetireRoute = "admin/windows/{id:guid}/exercises/{exerciseId:guid}/data/{datasetId:guid}/retire";
    private const string PutBackRoute = "admin/windows/{id:guid}/exercises/{exerciseId:guid}/data/{datasetId:guid}/put-back";

    // A GET confirmation step before each POST, as on ExerciseReleaseController: the Data tab sits
    // inside the exercise edit form, so it cannot hold a form of its own.
    [HttpGet(RetireRoute)]
    public Task<IActionResult> ConfirmRetire(Guid id, Guid exerciseId, Guid datasetId, CancellationToken cancellationToken)
        => ConfirmAsync(id, exerciseId, datasetId, retire: true, cancellationToken);

    [HttpGet(PutBackRoute)]
    public Task<IActionResult> ConfirmPutBack(Guid id, Guid exerciseId, Guid datasetId, CancellationToken cancellationToken)
        => ConfirmAsync(id, exerciseId, datasetId, retire: false, cancellationToken);

    /// <summary>
    /// Takes a slot out of use: its file has been replaced by another slot's (the 16-19 revised
    /// files replace the first four in February). No run reads it after this, so the next run
    /// makes a release without it. The slot is kept, because earlier releases name it.
    /// </summary>
    [HttpPost(RetireRoute)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Retire(Guid id, Guid exerciseId, Guid datasetId, CancellationToken cancellationToken)
        => SetRetiredAsync(id, exerciseId, datasetId, retired: true, cancellationToken);

    /// <summary>Puts a retired slot back in use, for an admin who retired the wrong one.</summary>
    [HttpPost(PutBackRoute)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> PutBackInUse(Guid id, Guid exerciseId, Guid datasetId, CancellationToken cancellationToken)
        => SetRetiredAsync(id, exerciseId, datasetId, retired: false, cancellationToken);

    private async Task<IActionResult> ConfirmAsync(
        Guid id, Guid exerciseId, Guid datasetId, bool retire, CancellationToken cancellationToken)
    {
        var window = await windows.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        var dataset = exercise?.Datasets.SingleOrDefault(d => d.Id == datasetId);
        if (window is null || exercise is null || dataset is null) return NotFound();

        var link = $"/admin/windows/{id}/exercises/{exerciseId}/data/{datasetId}/{(retire ? "retire" : "put-back")}";
        return View("~/Views/WindowAdmin/RetireDataset.cshtml", new RetireDatasetViewModel
        {
            Retire = retire,
            WindowTitle = window.Title,
            ExerciseName = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType),
            DatasetLabel = DatasetLabels.For(dataset.Name),
            PostUrl = link,
            CancelLink = $"/admin/windows/{id}/exercises/{exerciseId}/edit#{ExerciseLinks.DataTab}"
        });
    }

    // The validation stamp is left alone: the checksums cover only the slots in use, so the stamp
    // shows as stale until the exercise is run again, which is true.
    private async Task<IActionResult> SetRetiredAsync(
        Guid id, Guid exerciseId, Guid datasetId, bool retired, CancellationToken cancellationToken)
    {
        var window = await windows.GetByIdAsync(id, cancellationToken);
        var dataset = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId)?.Datasets.SingleOrDefault(d => d.Id == datasetId);
        if (window is null || dataset is null) return NotFound();

        dataset.Retired = retired;
        await windows.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Edit", "EditCheckingExercise", new { id, exerciseId }, ExerciseLinks.DataTab);
    }

    private void Decorate(AddExerciseDataItem model, CheckingWindowDto window, CheckingExerciseDto exercise)
    {
        model.ExerciseName = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType);
        model.IsResultsEnquiry = exercise.ExerciseType == CheckingExerciseType.ResultsEnquiry;
        model.FeedsJourney = WindowDatasets.AddedSlotFeedsJourney(exercise.ExerciseType, sourceFile: null);
        // Pupil inclusion applies only to pupil data, and a results source only to results. A data
        // share holds neither.
        model.AsksInclusion = exercise.ExerciseType == CheckingExerciseType.PupilData;
        model.SourceOptions = model.IsResultsEnquiry ? ResultsSources.For(window.CheckingWindowType) : [];
        // A window type with no results feed (KS2) has no sources to offer.
        model.AsksSource = model.SourceOptions.Count > 0;
        model.PostUrl = Url.Action("Submit", "ExerciseData", new { id = window.Id, exerciseId = exercise.Id });
        model.CancelUrl = Url.Action("Edit", "EditCheckingExercise", new { id = window.Id, exerciseId = exercise.Id }, null, null, ExerciseLinks.DataTab);
    }
}
