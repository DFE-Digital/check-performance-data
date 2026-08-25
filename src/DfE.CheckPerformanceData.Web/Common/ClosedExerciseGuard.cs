using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// #318: what a user is told when they reach a journey for a checking exercise that has closed.
/// The option list on Check your pupil data is presentation only — a bookmarked URL or a tab left
/// open across the closing date still posts — so every entry point rejects, and every rejection
/// lands back on Check your pupil data with a reason rather than on a 404.
/// </summary>
/// <remarks>
/// Wording lives in Web beside <c>NextStepLabels</c>: it is on-screen copy, not domain knowledge.
/// Whether an exercise is open is never decided here — that is
/// <see cref="Application.WindowManagement.ICheckingExerciseService"/>'s single job.
/// </remarks>
public static class ClosedExerciseGuard
{
    /// <summary>TempData slot read by <c>_ClosedExerciseBanner</c> on Check your pupil data.</summary>
    public const string TempDataKey = "ClosedExerciseMessage";

    /// <summary>
    /// The message for a rejected entry into <paramref name="exercise"/>. Closed removes actions,
    /// never content, so each message says what the user can still do.
    /// </summary>
    public static string MessageFor(CheckingExerciseType exercise) => exercise switch
    {
        CheckingExerciseType.PupilData =>
            "The deadline for requesting changes to your pupil data has passed. "
            + "You can still view and download your data.",
        CheckingExerciseType.ResultsEnquiry =>
            "The deadline for reporting an issue with your results has passed. "
            + "You can still view and download your data.",
        // No default, matching CheckingExerciseBlobPaths: a new exercise type must be given its own
        // wording rather than silently borrowing another exercise's. ClosedExerciseGuardTests pins
        // that every member of the enum has a message, so this cannot reach a user.
        _ => throw new ArgumentOutOfRangeException(
            nameof(exercise), exercise, "No closed-exercise message for this checking exercise.")
    };

    /// <summary>
    /// Rejects the request: stashes the reason and sends the user back to Check your pupil data.
    /// </summary>
    public static RedirectToActionResult RedirectExerciseClosed(
        this Controller controller, Guid windowId, CheckingExerciseType exercise)
    {
        controller.TempData[TempDataKey] = MessageFor(exercise);
        return controller.RedirectToAction("Index", "CheckYourPupilData", new { windowId });
    }
}
