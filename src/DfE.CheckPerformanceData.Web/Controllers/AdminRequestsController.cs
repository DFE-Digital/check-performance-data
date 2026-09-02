using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only admin list of the change requests in ONE checking window, showing each one's
// rules-engine outcome, with an optional filter by the window's checking exercises.
//
// Reached from the Requests link on each row of /admin/windows, not from the admin nav: a
// service-wide list of every request in every window could not answer the question an admin
// actually has, which is always about one window. Gated on the manage-window section for the
// same reason - whoever may see the windows table may follow its links.
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class AdminRequestsController(IAdminRequestsService service) : Controller
{
    [HttpGet("admin/windows/{windowId}/requests")]
    public async Task<IActionResult> Index(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken = default)
    {
        // No window, or one that no longer exists: back to the table the link came from, rather
        // than a 404 on a bookmarked or stale URL.
        var result = await service.GetForWindowAsync(windowId, exercise, cancellationToken);
        if (result is null)
            return RedirectToAction("Index", "WindowAdmin");

        return View(result);
    }
}
