using System.Globalization;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// AB#307773: closes one OPEN extension before its scheduled end, from the window details page, as
/// <see cref="CloseExerciseController"/> closes an exercise.
/// </summary>
/// <remarks>
/// Unlike the exercise's Close it runs no sweep. The issue has an extension's requests sent by Send
/// requests (<see cref="SendExtensionRequestsController"/>) or by the automatic hand-over after it
/// ends, so this only ends the extra time; the audit row is written with the close, by the service.
///
/// The confirmation is typing the window's name, with the same wording as the exercise's Close:
/// closing cannot be undone. Open is asked of <see cref="ICheckingExerciseService"/>, on the GET and
/// again on the POST.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class CloseExtensionController(
    IExerciseExtensionService extensions,
    IWindowService windowService,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUser) : Controller
{
    public const string NotOpenRefusal = "This extension is not open, so it was not closed.";

    private const string PageView = "~/Views/WindowAdmin/CloseExtension.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/close")]
    public async Task<IActionResult> Confirm(
        Guid id, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (ExerciseExtensionController.FindExtension(window, exerciseId, extensionId) is not (var row, var extension))
            return NotFound();

        // The summary offers Close early only while the extension is open, but a bookmarked URL or
        // a tab left open across the end date still arrives here.
        if (!checkingExercises.IsOpen(extension))
            return RefuseNotOpen(id);

        return View(PageView, Page(window!, row, extension, typed: null, error: null));
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        Guid id, Guid exerciseId, Guid extensionId, string? confirmWindowName, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (ExerciseExtensionController.FindExtension(window, exerciseId, extensionId) is not (var row, var extension)
            || window is null)
            return NotFound();

        if (!checkingExercises.IsOpen(extension))
            return RefuseNotOpen(id);

        // As on the exercise's Close: case-sensitive, white space either side trimmed from both.
        var typed = confirmWindowName?.Trim() ?? string.Empty;
        if (typed.Length == 0)
            return View(PageView, Page(window, row, extension, confirmWindowName, CloseExerciseController.NameRequiredError));
        if (!string.Equals(typed, window.Title.Trim(), StringComparison.Ordinal))
            return View(PageView, Page(window, row, extension, confirmWindowName, CloseExerciseController.NameMismatchError));

        var closure = await extensions.CloseEarlyAsync(
            id, exerciseId, extensionId, new ExtensionActor(currentUser.UserId, currentUser.DisplayName), cancellationToken);
        if (closure.Status != EarlyClosureStatus.Closed)
            return RefuseNotOpen(id);

        // The scheduled end the close replaced and the close's own time, on the UK clock every
        // exercise date is shown on (#535).
        TempData[ExerciseExtensionController.TempDataKey] =
            $"Extension of {CloseExerciseController.NameOf(row)} ending {Stamp(closure.ScheduledEnd)} " +
            $"was closed early on {Stamp(closure.ClosedAt)} by {currentUser.DisplayName}.";

        return Redirect($"/admin/windows/summary/{id}");
    }

    private static string Stamp(DateTime value) => value.ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture);

    private static CloseExtensionViewModel Page(
        CheckingWindowDto window, CheckingExerciseDto row, CheckingExerciseExtensionDto extension,
        string? typed, string? error) => new()
    {
        WindowId = window.Id,
        ExerciseId = row.Id,
        ExtensionId = extension.Id,
        WindowTitle = window.Title,
        ExerciseLabel = CloseExerciseController.NameOf(row),
        ScheduledEnd = extension.EndDate,
        SchoolCount = extension.SchoolCount,
        ConfirmWindowName = typed,
        Error = error
    };

    // Not an error page: the extension simply is not open any more. The summary shows where it stands.
    private RedirectResult RefuseNotOpen(Guid id)
    {
        TempData[SendExerciseRequestsController.RefusedTempDataKey] = NotOpenRefusal;
        return Redirect($"/admin/windows/summary/{id}");
    }
}
