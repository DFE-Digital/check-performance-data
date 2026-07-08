using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Admin-native replacement for the old /help/deleted wiki view: lists every soft-deleted
// PageNode with a Restore action so a caller with the deleted-pages section grant can undo
// an accidental delete without touching the database.
[RequireAdminSection(AdminNavKeys.DeletedPages)]
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
