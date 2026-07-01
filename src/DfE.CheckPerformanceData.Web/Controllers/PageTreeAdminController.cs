using DfE.CheckPerformanceData.Application.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class PageTreeAdminController(IPageNodeService pageNodeService) : Controller
{
    [HttpGet("/admin/pages")]
    public async Task<IActionResult> Index()
    {
        var tree = PageTreeBuilder.Build(await pageNodeService.GetTreeAsync());
        return View(tree);
    }
}
