using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
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
        var all = navEntries.ToList();
        var groups = all
            .Where(e => e.ParentKey is null)
            .OrderBy(e => e.Order)
            .Select(g => new AdminNavGroupViewModel
            {
                Key = g.Key,
                Title = g.Title,
                Description = g.Description,
                Children = all
                    .Where(e => e.ParentKey == g.Key)
                    .OrderBy(e => e.Order)
                    .ToList()
            })
            .ToList();

        return View(new AdminLandingViewModel { Groups = groups });
    }
}
