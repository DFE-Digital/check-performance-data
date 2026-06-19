using DfE.CheckPerformanceData.Application.UncommittedRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only admin list of SubmittedUnCommitted change requests for the current open
// checking window, showing each one's rules-engine outcome. Gated by cypmd_admin.
[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class AdminUncommittedRequestsController(IUncommittedRequestsService service) : Controller
{
    [HttpGet("admin/uncommitted-requests")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var rows = await service.GetAsync(cancellationToken);
        return View(rows);
    }
}
