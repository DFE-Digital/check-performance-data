using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Admin-native replacement for the old /help/deleted wiki view: lists every soft-deleted
// PageNode with a Restore action so an admin can undo an accidental delete without touching
// the database.
[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class DeletedPagesController(IPageNodeService pageNodes) : Controller
{
    [HttpGet("/admin/pages/deleted")]
    public async Task<IActionResult> Index()
    {
        ViewData["AdminActiveKey"] = Admin.Nav.AdminNavKeys.DeletedPages;
        var deleted = await pageNodes.GetDeletedAsync();
        return View(new DeletedPagesViewModel
        {
            Deleted = deleted,
            SuccessMessage = TempData["DeletedPagesMessage"] as string,
        });
    }

    [HttpPost("/admin/pages/deleted/{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id)
    {
        await pageNodes.RestoreAsync(id, User?.Identity?.Name);
        TempData["DeletedPagesMessage"] = "Page restored.";
        return RedirectToAction(nameof(Index));
    }
}
