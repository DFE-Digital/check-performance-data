using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// AB#307773: sends a CLOSED extension's submitted requests for processing and cancels its schools'
/// leftover drafts, as <see cref="SendExerciseRequestsController"/> does for an exercise.
/// </summary>
/// <remarks>
/// The sweep takes only the extension's schools, and stamps the extension sent so anything of theirs
/// left over goes with whichever sweep runs next. The automatic hand-over does the same after the
/// extension ends; this is for an admin who wants it sooner, or again.
///
/// The audit row is written after the sweep, in its own transaction: the sweep is not
/// transactional (a blob read and a queue write per request), and the row records what it did. If
/// the sweep fails there is nothing to record, and the admin is told to try again rather than shown
/// an error page. If the row cannot be written the send has still happened, so the admin still sees
/// what it did; the failure is logged, as the automatic hand-over logs its own.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class SendExtensionRequestsController(
    ICloseExerciseService closeService,
    IExerciseExtensionRepository extensions,
    IWindowService windowService,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<SendExtensionRequestsController> logger) : Controller
{
    public const string NotClosedRefusal = "This extension has not closed, so no requests were sent.";

    public const string SweepFailedRefusal =
        "The extension's requests could not be sent for processing. Select Send requests for processing to try again.";

    private const string PageView = "~/Views/WindowAdmin/SendExtensionRequests.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/send-requests")]
    public async Task<IActionResult> Confirm(
        Guid id, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (ExerciseExtensionController.FindExtension(window, exerciseId, extensionId) is not (var row, var extension))
            return NotFound();

        // While the extension is open its schools could add requests behind the sweep.
        if (!checkingExercises.HasClosed(extension))
            return RefuseNotClosed(id);

        var preview = await closeService.PreviewExtensionAsync(id, exerciseId, extensionId, cancellationToken);

        return View(PageView, new SendExtensionRequestsViewModel
        {
            WindowId = id,
            ExerciseId = exerciseId,
            ExtensionId = extensionId,
            WindowTitle = window!.Title,
            ExerciseLabel = CloseExerciseController.NameOf(row),
            ExtensionEnd = extension.EndDate,
            SchoolCount = extension.SchoolCount,
            RequestsToSend = preview.RequestsToClose,
            DraftsToCancel = preview.DraftsToCancel,
            RequestsWaiting = preview.RequestsWaiting
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/send-requests")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        Guid id, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        // Re-checked on the POST: the confirmation page is not what authorises the sweep, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (ExerciseExtensionController.FindExtension(window, exerciseId, extensionId) is not (var row, var extension))
            return NotFound();

        if (!checkingExercises.HasClosed(extension))
            return RefuseNotClosed(id);

        CloseExerciseResult result;
        try
        {
            result = await closeService.CloseExtensionAsync(id, exerciseId, extensionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failed part-way or outright; the service has not stamped the extension sent, so a
            // retry sweeps whatever is left. Ids only in the log — no pupil data.
            logger.LogError(ex,
                "Sending the requests of extension {ExtensionId} of checking exercise {ExerciseId} of window {WindowId} for processing failed",
                extensionId, exerciseId, id);
            TempData[SendExerciseRequestsController.RefusedTempDataKey] = SweepFailedRefusal;
            return Redirect($"/admin/windows/summary/{id}");
        }

        try
        {
            // Deliberately not the request's token: the requests have been sent and cannot be
            // recalled, so their record must not be abandoned because the browser went away.
            await extensions.RecordRequestsSentAsync(new ExtensionRequestsSent
            {
                WindowId = id,
                ExerciseId = exerciseId,
                Exercise = row.ExerciseType!.Value,
                ExtensionId = extensionId,
                ExtensionEnd = extension.EndDate,
                RequestsSent = result.Enqueued,
                DraftsCancelled = result.DraftsCancelled,
                Actor = new ExtensionActor(currentUser.UserId, currentUser.DisplayName),
                SentAtUtc = timeProvider.GetUtcNow().UtcDateTime
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The send has happened, so the admin is told what it did rather than shown an error
            // page that would have them send again. The log line is then the only record of it.
            // Ids only — no pupil data.
            logger.LogError(ex,
                "The requests of extension {ExtensionId} of checking exercise {ExerciseId} of window {WindowId} were sent for processing, but the audit row could not be written",
                extensionId, exerciseId, id);
        }

        // Quotes the RESULT, never the preview — rows can change between the two.
        TempData[CloseExerciseController.TempDataKey] =
            $"{ExerciseExtensionController.Pluralise(result.Enqueued, "request")} sent for processing and " +
            $"{ExerciseExtensionController.Pluralise(result.DraftsCancelled, "draft")} cancelled " +
            $"for the extension of {CloseExerciseController.NameOf(row)}." +
            SendExerciseRequestsController.WaitingSentence(result.Waiting);

        return Redirect($"/admin/windows/summary/{id}");
    }

    private RedirectResult RefuseNotClosed(Guid id)
    {
        TempData[SendExerciseRequestsController.RefusedTempDataKey] = NotClosedRefusal;
        return Redirect($"/admin/windows/summary/{id}");
    }
}
