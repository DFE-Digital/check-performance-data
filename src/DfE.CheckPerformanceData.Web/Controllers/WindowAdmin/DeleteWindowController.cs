using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Deletes a whole checking window from the window details page.
/// </summary>
/// <remarks>
/// A GET "are you sure" step before the POST, as <see cref="CloseExerciseController"/> does: the
/// delete cannot be undone, so the admin first sees the change requests and egress runs it takes
/// with it. When there are requests, the admin must also tick a box to say so.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class DeleteWindowController(
    IDeleteWindowService deleteService,
    IWindowService windowService) : Controller
{
    /// <summary>Carries the outcome sentence to the notification banner on the windows list.</summary>
    public const string TempDataKey = "DeleteWindowOutcome";

    private const string ConfirmError = "Confirm that you want to delete the change requests made through this window";

    [HttpGet("admin/windows/{id:guid}/delete")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
            return NotFound();

        return await ConfirmView(window, confirmRequestsDeleted: false, cancellationToken);
    }

    [HttpPost("admin/windows/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        Guid id, bool confirmRequestsDeleted, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
            return NotFound();

        // Counted again on the POST: a school may have made a request since the page was drawn,
        // and an admin who saw none must not delete one without being asked.
        var preview = await deleteService.PreviewAsync(id, cancellationToken);
        if (preview.HasRequests && !confirmRequestsDeleted)
        {
            ModelState.AddModelError(nameof(DeleteWindowViewModel.ConfirmRequestsDeleted), ConfirmError);
            return ConfirmView(window, preview, confirmRequestsDeleted);
        }

        if (!await deleteService.DeleteAsync(id, cancellationToken))
            return NotFound();

        TempData[TempDataKey] = $"{window.Title} deleted.";
        return Redirect("/admin/windows");
    }

    private async Task<IActionResult> ConfirmView(
        CheckingWindowDto window, bool confirmRequestsDeleted, CancellationToken cancellationToken) =>
        ConfirmView(window, await deleteService.PreviewAsync(window.Id, cancellationToken), confirmRequestsDeleted);

    private ViewResult ConfirmView(CheckingWindowDto window, DeleteWindowPreview preview, bool confirmRequestsDeleted) =>
        View("~/Views/WindowAdmin/Delete.cshtml", new DeleteWindowViewModel
        {
            WindowId = window.Id,
            WindowTitle = window.Title,
            ExerciseCount = window.Exercises.Count,
            Preview = preview,
            ConfirmRequestsDeleted = confirmRequestsDeleted
        });
}
