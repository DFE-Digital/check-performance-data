using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Deletes one checking exercise from the window details page.
/// </summary>
/// <remarks>
/// A GET "are you sure" step before the POST, as <see cref="DeleteWindowController"/> does: the
/// delete cannot be undone, so the admin first sees what goes with it. When there are change
/// requests, the admin must also tick a box to say so.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class DeleteExerciseController(
    IDeleteExerciseService deleteService,
    IWindowService windowService,
    ICheckingExerciseService checkingExercises) : Controller
{
    /// <summary>Carries the outcome sentence to the notification banner on the summary page.</summary>
    public const string TempDataKey = "DeleteExerciseOutcome";

    private const string ConfirmError = "Confirm that you want to delete the change requests made through this exercise";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/delete")]
    public async Task<IActionResult> Confirm(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null)
            return NotFound();

        var preview = await deleteService.PreviewAsync(exerciseId, cancellationToken);
        return ConfirmView(window, exercise, preview, confirmRequestsDeleted: false);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        Guid id, Guid exerciseId, bool confirmRequestsDeleted, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null)
            return NotFound();

        // Counted again on the POST: a school may have made a request since the page was drawn,
        // and an admin who saw none must not delete one without being asked.
        var preview = await deleteService.PreviewAsync(exerciseId, cancellationToken);
        if (preview.HasRequests && !confirmRequestsDeleted)
        {
            ModelState.AddModelError(nameof(DeleteExerciseViewModel.ConfirmRequestsDeleted), ConfirmError);
            return ConfirmView(window, exercise, preview, confirmRequestsDeleted);
        }

        if (!await deleteService.DeleteAsync(id, exerciseId, cancellationToken))
            return NotFound();

        TempData[TempDataKey] = $"{NameOf(exercise)} deleted.";
        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string NameOf(CheckingExerciseDto exercise) =>
        exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType);

    private ViewResult ConfirmView(
        CheckingWindowDto window, CheckingExerciseDto exercise, DeleteExercisePreview preview,
        bool confirmRequestsDeleted) =>
        View("~/Views/WindowAdmin/DeleteExercise.cshtml", new DeleteExerciseViewModel
        {
            WindowId = window.Id,
            ExerciseId = exercise.Id,
            WindowTitle = window.Title,
            ExerciseName = NameOf(exercise),
            KindLabel = ExerciseLabels.For(exercise.ExerciseType),
            ExerciseType = exercise.ExerciseType,
            IsOpen = checkingExercises.IsOpen(exercise),
            HasClosed = checkingExercises.HasClosed(exercise),
            IsVisibleToSchools =
                checkingExercises.StatusOf(window.Exercises, exercise) != ExerciseSchoolStatus.Hidden,
            UsesExerciseStorage = exercise.UsesExerciseStorage,
            DatasetCount = exercise.Datasets.Count,
            ReleaseCount = exercise.Releases.Count,
            IsLastExercise = window.Exercises.Count == 1,
            ReplacedBy = window.Exercises
                .Where(e => e.ReplacesCheckingExerciseId == exercise.Id)
                .Select(NameOf)
                .ToList(),
            Preview = preview,
            ConfirmRequestsDeleted = confirmRequestsDeleted
        });
}
