using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.ExerciseDecisions;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Pulls the decision Zendesk holds for each submitted request of one checking exercise and
/// stores the final ones (#746).
/// </summary>
/// <remarks>
/// A GET confirmation step before the POST, as on <see cref="CloseExerciseController"/> and
/// <see cref="SendExerciseRequestsController"/>: the pull overwrites stored decisions, so the
/// admin sees what it will touch.
///
/// Addressed by exercise id, like Close (#466); a data share's id is not found here.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class PullExerciseDecisionsController(
    IWindowService windowService,
    IExerciseDecisionPullService pullService) : Controller
{
    /// <summary>A completed pull: the five bucket counts, on a success banner at the summary.</summary>
    public const string SuccessTempDataKey = "PullDecisionsSuccess";

    /// <summary>A refused pull: the source's failure reason, on a neutral banner at the summary.</summary>
    public const string FailedTempDataKey = "PullDecisionsFailed";

    private const string PageView = "~/Views/WindowAdmin/PullDecisions.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/pull-decisions")]
    public async Task<IActionResult> Confirm(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.Exercises.SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null) is not { } row)
            return NotFound();

        var preview = await pullService.PreviewAsync(id, exerciseId, cancellationToken);

        return View(PageView, new PullDecisionsViewModel
        {
            WindowId = id,
            ExerciseId = exerciseId,
            WindowTitle = window.Title,
            ExerciseLabel = CloseExerciseController.NameOf(row),
            SubmittedTotal = preview.SubmittedTotal,
            WithTicket = preview.WithTicket
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/pull-decisions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pull(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        // Re-checked on the POST, not only on the GET: the confirmation page is not what
        // authorises the pull, the route is.
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window?.Exercises.SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null) is not { } row)
            return NotFound();

        var result = await pullService.PullAsync(id, exerciseId, cancellationToken);

        // Quotes the RESULT, never the preview — rows can change between the two.
        if (result.FailureReason is not null)
        {
            TempData[FailedTempDataKey] =
                $"Decisions could not be pulled for {CloseExerciseController.NameOf(row)}. " +
                $"{result.FailureReason} No request was changed.";
        }
        else
        {
            TempData[SuccessTempDataKey] =
                $"{result.Accepted} accepted, {result.Rejected} rejected, {result.Unchanged} still under review, " +
                $"{result.NoTicket} with no ticket, {result.TicketNotFound} tickets not found, " +
                $"for {CloseExerciseController.NameOf(row)}.";
        }

        return Redirect($"/admin/windows/summary/{id}");
    }
}
