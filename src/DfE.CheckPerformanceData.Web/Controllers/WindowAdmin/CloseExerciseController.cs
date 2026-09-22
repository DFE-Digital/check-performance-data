using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Closes one checking exercise from the window details page.
/// </summary>
/// <remarks>
/// Replaces the disabled "Process Close Window" button that used to sit on the Amendment Requests
/// page. That button drove a sweep of every OPEN window at once, which no per-window page could
/// honestly offer; closing is now scoped to the window and exercise named in the route.
///
/// A GET confirmation step before the POST, matching <see cref="ValidateWindowController"/> on the
/// same page: the sweep is irreversible and dispatches to an external system, so the admin sees
/// what it will touch first.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class CloseExerciseController(
    ICloseExerciseService closeService,
    IWindowService windowService) : Controller
{
    /// <summary>Carries the outcome sentence to the notification banner on the summary page.</summary>
    public const string TempDataKey = "CloseExerciseOutcome";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/close")]
    public async Task<IActionResult> Confirm(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var target = window?.FindExercise(exerciseId);

        // A display-only exercise has no journeys, so nothing to sweep: not found, same as an
        // exercise the window does not run. (ValidateWindowController treats the same exercise
        // differently — 400, not 404 — because there it exists and is simply not ready; see the
        // note there.)
        if (window is null || target?.ExerciseType is not { } kind)
            return NotFound();

        var preview = await closeService.PreviewAsync(id, kind, cancellationToken);

        return View("~/Views/WindowAdmin/Close.cshtml", new CloseExerciseViewModel
        {
            WindowId = id,
            WindowTitle = window.Title,
            ExerciseId = exerciseId,
            ExerciseLabel = target.Name,
            RequestsToClose = preview.RequestsToClose,
            DraftsToCancel = preview.DraftsToCancel
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        // Re-checked on the POST, not only on the GET: the confirmation page is not what authorises
        // the sweep, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var target = window?.FindExercise(exerciseId);

        if (window is null || target?.ExerciseType is not { } kind)
            return NotFound();

        var result = await closeService.CloseAsync(id, kind, cancellationToken);

        // Quotes the RESULT, never the preview — rows can change between the two.
        TempData[TempDataKey] =
            $"{target.Name} closed. " +
            $"{Pluralise(result.Enqueued, "request")} sent for processing and " +
            $"{Pluralise(result.DraftsCancelled, "draft")} cancelled.";

        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string Pluralise(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
