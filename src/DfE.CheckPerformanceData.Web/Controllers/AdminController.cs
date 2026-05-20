using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Administration shell landing page. Gated by the cypmd_admin role so editors and
// unprivileged users cannot reach the admin surface. Feature-area phases attach their
// own controllers under /admin/<feature> with the same [Authorize(Roles = ...)] attribute.
public sealed class AdminController(IEnumerable<IAdminNavEntry> navEntries) : Controller
{
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin")]
    public IActionResult Index()
    {
        IReadOnlyList<IAdminNavEntry> sorted = navEntries.OrderBy(e => e.Order).ToList();
        return View(sorted);
    }
}
