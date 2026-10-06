using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Sends a CLOSED checking exercise's submitted requests for processing and cancels its leftover
/// drafts (AB#301022).
/// </summary>
/// <remarks>
/// This is the sweep the Close action runs after it ends an exercise early, and the sweep the
/// service runs by itself two hours after any exercise ends (AB#302158, ExerciseHandOverJob). It
/// is still offered on its own, for an admin who wants the requests sent sooner than that, or
/// again — it is the only way a pupil-data amendment reaches Zendesk, and the automatic run stops
/// trying a day after its first attempt (26 hours after the exercise ends). While an exercise is
/// still open the sweep belongs to Close, which ends the exercise first; sweeping an open
/// exercise would commit requests while schools could still add more.
///
/// A GET confirmation step before the POST, as on <see cref="CloseExerciseController"/>: the sweep
/// is irreversible and dispatches to an external system, so the admin sees what it will touch.
///
/// Addressed by exercise id, like Close (#466); a data share's id is not found here.
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

    /// <summary>
    /// The banner's sentence for requests the sweep left because the Rules Engine had not decided
    /// them (#536), with a leading space; empty when there are none. Shared with
    /// <see cref="CloseExerciseController"/>, whose sweep leaves them the same way.
    /// </summary>
    public static string WaitingSentence(int waiting) => waiting switch
    {
        0 => string.Empty,
        1 => " 1 request is waiting for the Rules Engine. Send requests for processing again later.",
        _ => $" {waiting} requests are waiting for the Rules Engine. Send requests for processing again later."
    };

    private const string PageView = "~/Views/WindowAdmin/SendRequests.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/send-requests")]
    public async Task<IActionResult> Confirm(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.Exercises.SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null) is not { } row)
            return NotFound();

        if (!checkingExercises.HasClosed(row))
            return RefuseNotClosed(id, row);

        var preview = await closeService.PreviewAsync(id, exerciseId, cancellationToken);

        return View(PageView, new SendRequestsViewModel
        {
            WindowId = id,
            ExerciseId = exerciseId,
            WindowTitle = window.Title,
            ExerciseLabel = CloseExerciseController.NameOf(row),
            RequestsToSend = preview.RequestsToClose,
            DraftsToCancel = preview.DraftsToCancel,
            RequestsWaiting = preview.RequestsWaiting
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/send-requests")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        // Re-checked on the POST, not only on the GET: the confirmation page is not what authorises
        // the sweep, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.Exercises.SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null) is not { } row)
            return NotFound();

        if (!checkingExercises.HasClosed(row))
            return RefuseNotClosed(id, row);

        var result = await closeService.CloseAsync(id, exerciseId, cancellationToken);

        // Quotes the RESULT, never the preview — rows can change between the two.
        TempData[CloseExerciseController.TempDataKey] =
            $"{Pluralise(result.Enqueued, "request")} sent for processing and " +
            $"{Pluralise(result.DraftsCancelled, "draft")} cancelled for {CloseExerciseController.NameOf(row)}." +
            WaitingSentence(result.Waiting);

        return Redirect($"/admin/windows/summary/{id}");
    }

    private RedirectResult RefuseNotClosed(Guid id, CheckingExerciseDto row)
    {
        TempData[RefusedTempDataKey] = $"{CloseExerciseController.NameOf(row)} has not closed, so no requests were sent.";
        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string Pluralise(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
