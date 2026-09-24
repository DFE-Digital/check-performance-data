using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Makes one release of a checking exercise the release schools see.
/// </summary>
/// <remarks>
/// A GET confirmation step before the POST, as on <see cref="CloseExerciseController"/>: the change
/// reaches every school at once, so the admin sees which release goes live and which one it
/// replaces first. Nothing is deleted, so the change can be reversed the same way.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class ExerciseReleaseController(
    IWindowService windowService,
    ICheckingExerciseReleaseService releases) : Controller
{
    private const string Route = "admin/windows/{id:guid}/exercises/{exerciseId:guid}/releases/{releaseId:guid}/make-live";

    [HttpGet(Route)]
    public async Task<IActionResult> Confirm(Guid id, Guid exerciseId, Guid releaseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        var release = exercise?.Releases.SingleOrDefault(r => r.Id == releaseId);
        if (window is null || exercise is null || release is null)
            return NotFound();

        return View("~/Views/WindowAdmin/MakeReleaseLive.cshtml", new MakeReleaseLiveViewModel
        {
            WindowId = id,
            ExerciseId = exerciseId,
            WindowTitle = window.Title,
            ExerciseName = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType),
            Release = release,
            LiveRelease = exercise.CurrentRelease
        });
    }

    [HttpPost(Route)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakeLive(Guid id, Guid exerciseId, Guid releaseId, CancellationToken cancellationToken)
    {
        // The service checks the window, the exercise and the release belong together. The
        // confirmation page is not what authorises the change, the route is.
        var result = await releases.MakeLiveAsync(id, exerciseId, releaseId, cancellationToken);
        if (result == MakeReleaseLiveResult.NotFound)
            return NotFound();

        return Redirect($"/admin/windows/{id}/exercises/{exerciseId}/edit#releases");
    }
}
