using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Administration shell landing page. Gated against the AdminSectionAccess grid — any role
// with at least one section grant may reach the landing. The sidebar and tile grid then hide
// sections the user's roles do not grant, so the landing degrades per-role rather than a
// hard "admin only" wall.
[RequireAdminSection(AllowAnyGrantedSection = true)]
public sealed class AdminController(IEnumerable<IAdminNavEntry> navEntries) : Controller
{
    [HttpGet("admin")]
    public IActionResult Index()
    {
        // Build the full nav forest (shared with the sidebar) so the landing page can recurse
        // into container sub-groups such as Rules Engine rather than rendering them as dead links.
        var roots = AdminNavNodeViewModel.BuildForest(navEntries);
        return View(new AdminLandingViewModel { Roots = roots });
    }
}
