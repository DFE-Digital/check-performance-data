using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Sends a CLOSED checking exercise's submitted requests for processing and cancels its leftover
/// drafts (AB#301022).
/// </summary>
/// <remarks>
/// This is the sweep the Close action runs after it ends an exercise early. It is also offered on
/// its own because nothing runs it when an exercise reaches its scheduled end, and it is the only
/// way a pupil-data amendment reaches Zendesk — so without this page a scheduled close could never
/// be handed over. While an exercise is still open the sweep belongs to Close, which ends the
/// exercise first; sweeping an open exercise would commit requests while schools could still add
/// more.
///
/// A GET confirmation step before the POST, as on <see cref="CloseExerciseController"/>: the sweep
/// is irreversible and dispatches to an external system, so the admin sees what it will touch.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class SendExerciseRequestsController(
    ICloseExerciseService closeService,
    IWindowService windowService,
    ICheckingExerciseService checkingExercises) : Controller
{
    /// <summary>
    /// Carries "nothing happened, and why" to a neutral banner on the summary page. Shared with
    /// <see cref="CloseExerciseController"/>: both refuse by sending the admin back there.
    /// </summary>
    public const string RefusedTempDataKey = "CloseExerciseRefused";

    private const string PageView = "~/Views/WindowAdmin/SendRequests.cshtml";

    [HttpGet("admin/windows/{id:guid}/{exercise}/send-requests")]
    public async Task<IActionResult> Confirm(
        Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.FindExercise(exercise) is null)
            return NotFound();

        if (!checkingExercises.HasClosed(window.Exercises, exercise))
            return RefuseNotClosed(id, exercise);

        var preview = await closeService.PreviewAsync(id, exercise, cancellationToken);

        return View(PageView, new SendRequestsViewModel
        {
            WindowId = id,
            WindowTitle = window.Title,
            ExerciseType = exercise,
            ExerciseLabel = ExerciseLabels.For(exercise),
            RequestsToSend = preview.RequestsToClose,
            DraftsToCancel = preview.DraftsToCancel
        });
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/send-requests")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        // Re-checked on the POST, not only on the GET: the confirmation page is not what authorises
        // the sweep, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.FindExercise(exercise) is null)
            return NotFound();

        if (!checkingExercises.HasClosed(window.Exercises, exercise))
            return RefuseNotClosed(id, exercise);

        var result = await closeService.CloseAsync(id, exercise, cancellationToken);

        // Quotes the RESULT, never the preview — rows can change between the two.
        TempData[CloseExerciseController.TempDataKey] =
            $"{Pluralise(result.Enqueued, "request")} sent for processing and " +
            $"{Pluralise(result.DraftsCancelled, "draft")} cancelled for {ExerciseLabels.For(exercise)}.";

        return Redirect($"/admin/windows/summary/{id}");
    }

    private RedirectResult RefuseNotClosed(Guid id, CheckingExerciseType exercise)
    {
        TempData[RefusedTempDataKey] = $"{ExerciseLabels.For(exercise)} has not closed, so no requests were sent.";
        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string Pluralise(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
