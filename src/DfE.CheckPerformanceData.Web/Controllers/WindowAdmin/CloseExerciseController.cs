using System.Globalization;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Closes one OPEN checking exercise before its scheduled end, from the window details page
/// (AB#301022).
/// </summary>
/// <remarks>
/// Closing does two things, in this order:
/// <list type="number">
/// <item>ends the exercise now (<see cref="IExerciseEarlyClosureService"/>), which is what stops
/// schools acting on it and what writes the audit row;</item>
/// <item>hands its requests over (<see cref="ICloseExerciseService"/>): submitted requests go for
/// processing and leftover drafts are cancelled — what "Close" has done since #437.</item>
/// </list>
/// The order matters. Sweeping first would commit requests while schools could still add more
/// behind the sweep. If the sweep then fails, the exercise is already closed and recorded; the
/// admin is told so, and told that the sweep can be run again from
/// <see cref="SendExerciseRequestsController"/>, rather than being shown an error page.
///
/// The confirmation is typing the window's name: closing cannot be undone, and one press on a
/// button is too easy to give by accident. Who may close is the class-level gate's concern.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class CloseExerciseController(
    ICloseExerciseService closeService,
    IExerciseEarlyClosureService earlyClosure,
    IWindowService windowService,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUser,
    ILogger<CloseExerciseController> logger) : Controller
{
    /// <summary>Carries the outcome sentence to the success banner on the summary page.</summary>
    public const string TempDataKey = "CloseExerciseOutcome";

    // FLAGGED copy (AB#301022).
    public const string NameRequiredError = "Enter the window name";
    public const string NameMismatchError = "The window name you entered does not match";

    private const string PageView = "~/Views/WindowAdmin/Close.cshtml";

    [HttpGet("admin/windows/{id:guid}/{exercise}/close")]
    public async Task<IActionResult> Confirm(
        Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.FindExercise(exercise) is not { } row)
            return NotFound();

        // The summary page offers Close only while the exercise is open, but a bookmarked URL or a
        // tab left open across the end date still arrives here.
        if (!checkingExercises.IsOpen(window.Exercises, exercise))
            return RefuseNotOpen(id, exercise);

        return View(PageView, Page(window, row, typed: null, error: null));
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        Guid id, CheckingExerciseType exercise, string? confirmWindowName, CancellationToken cancellationToken)
    {
        // Re-checked on the POST, not only on the GET: the confirmation page is not what authorises
        // the close, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.FindExercise(exercise) is not { } row)
            return NotFound();

        if (!checkingExercises.IsOpen(window.Exercises, exercise))
            return RefuseNotOpen(id, exercise);

        // "Exactly" is case-sensitive. White space either side is trimmed from both: it is what a
        // copy and paste adds, and no admin could see it to correct it.
        var typed = confirmWindowName?.Trim() ?? string.Empty;
        if (typed.Length == 0)
            return View(PageView, Page(window, row, confirmWindowName, NameRequiredError));
        if (!string.Equals(typed, window.Title.Trim(), StringComparison.Ordinal))
            return View(PageView, Page(window, row, confirmWindowName, NameMismatchError));

        var closure = await earlyClosure.CloseEarlyAsync(
            id, exercise, new EarlyClosureActor(currentUser.UserId, currentUser.DisplayName), cancellationToken);
        if (closure.Status != EarlyClosureStatus.Closed)
            return RefuseNotOpen(id, exercise);

        // When the close took effect, on the same clock as every exercise date on the page: the
        // server's. That is UTC in the containers today, not UK time (see docs/16-19-window-model.md).
        var closed =
            $"{ExerciseLabels.For(exercise)} was closed early on " +
            $"{closure.ClosedAt.ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture)} by {currentUser.DisplayName}.";

        CloseExerciseResult sweep;
        try
        {
            sweep = await closeService.CloseAsync(id, exercise, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The close and its audit row are committed; the sweep is not transactional (a blob
            // read and a queue write per request) and has failed part-way or outright. An error
            // page here would leave the admin not knowing the exercise had closed. Ids only in
            // the log — no pupil data.
            logger.LogError(ex,
                "Checking exercise {Exercise} of window {WindowId} was closed early, but sending its requests for processing failed",
                exercise, id);

            // The neutral banner, not the success one: only half of what Close does has happened.
            TempData[SendExerciseRequestsController.RefusedTempDataKey] =
                $"{closed} Its requests could not be sent for processing. " +
                $"Select Send {ExerciseLabels.For(exercise)} requests for processing to try again.";
            return Redirect($"/admin/windows/summary/{id}");
        }

        // Quotes the RESULTS, never a prediction: what the sweep actually did.
        TempData[TempDataKey] =
            $"{closed} " +
            $"{Pluralise(sweep.Enqueued, "request")} sent for processing and " +
            $"{Pluralise(sweep.DraftsCancelled, "draft")} cancelled." +
            SendExerciseRequestsController.WaitingSentence(sweep.Waiting);

        return Redirect($"/admin/windows/summary/{id}");
    }

    private static CloseExerciseViewModel Page(
        CheckingWindowDto window, CheckingExerciseDto row, string? typed, string? error) => new()
    {
        WindowId = window.Id,
        WindowTitle = window.Title,
        ExerciseType = row.ExerciseType,
        ExerciseLabel = ExerciseLabels.For(row.ExerciseType),
        ScheduledEnd = row.EndDate,
        ConfirmWindowName = typed,
        Error = error
    };

    // Not an error page: the admin did nothing wrong, the exercise simply is not open any more (or
    // not yet). They go back to the summary, which shows its status and what can be done instead.
    private RedirectResult RefuseNotOpen(Guid id, CheckingExerciseType exercise)
    {
        TempData[SendExerciseRequestsController.RefusedTempDataKey] =
            $"{ExerciseLabels.For(exercise)} is not open, so it was not closed.";
        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string Pluralise(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
