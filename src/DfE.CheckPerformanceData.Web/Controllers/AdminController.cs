using DfE.CheckPerformanceData.Application.Admin;
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
public sealed class AdminController(
    IEnumerable<IAdminNavEntry> navEntries,
    IAdminAccessPolicy accessPolicy) : Controller
{
    [HttpGet("admin")]
    public async Task<IActionResult> Index()
    {
        // Build the full nav forest (shared with the sidebar) so the landing page can recurse
        // into container sub-groups such as Rules Engine rather than rendering them as dead links.
        var roots = AdminNavNodeViewModel.BuildForest(navEntries);

        // Then apply the same grant filter the sidebar applies. Rendering the unfiltered forest
        // showed every tile to every user with any grant, and led an editor-only user to the
        // window-administration pages their role does not have.
        var access = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var key in AdminNavNodeViewModel.CollectEntries(roots).Select(e => e.Key).Distinct(StringComparer.Ordinal))
            access[key] = await accessPolicy.CanAccessAsync(User, key);

        var visible = AdminNavNodeViewModel.FilterByAccess(roots, key => access.TryGetValue(key, out var ok) && ok);
        return View(new AdminLandingViewModel { Roots = visible });
    }
}
