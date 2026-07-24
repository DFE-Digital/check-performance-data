using DfE.CheckPerformanceData.Application.UncommittedRequests;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only admin list of change requests across every checking window, showing each
// one's rules-engine outcome. Gated by the uncommitted-requests section grant.
[RequireAdminSection(AdminNavKeys.UncommittedRequests)]
public sealed class AdminRequestsController(IAdminRequestsService service) : Controller
{
    [HttpGet("admin/uncommitted-requests")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var rows = await service.GetAsync(cancellationToken);
        return View(rows);
    }

    // Quick-and-dirty test hook: drop a RequestDocument for every SubmittedUnCommitted
    // request in the current open window(s) onto the Zendesk queue.
    [HttpPost("admin/uncommitted-requests/send-to-zendesk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToZendesk(CancellationToken cancellationToken = default)
    {
        var count = await service.ProcessCloseWindowEvent(cancellationToken);
        TempData["ZendeskReplayMessage"] = $"Queued {count} request(s) onto the Zendesk queue.";
        return RedirectToAction(nameof(Index));
    }
}
