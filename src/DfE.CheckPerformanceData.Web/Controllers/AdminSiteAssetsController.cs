using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Where an administrator edits the site-wide CSS and JavaScript. Doubly gated: the section
// grant puts it in the role-access grid like every other admin page, and the administrator role
// is required regardless of the grid, because a script here runs on every page for every user and
// so is not something to hand to editors by ticking a box. Views live under Views/Admin/SiteAssets
// so they inherit the admin layout.
[RequireAdminSection(AdminNavKeys.SiteAssets)]
[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class AdminSiteAssetsController(ISiteAssetService assets, ILogger<AdminSiteAssetsController> logger) : Controller
{
    private const string IndexView = "~/Views/Admin/SiteAssets/Index.cshtml";

    [HttpGet("admin/site-assets")]
    public async Task<IActionResult> Index()
    {
        var content = await assets.GetAsync();
        return View(IndexView, new AdminSiteAssetsViewModel
        {
            Css = content.Css,
            Js = content.Js,
            CssEnabled = content.CssEnabled,
            JsEnabled = content.JsEnabled,
        });
    }

    [HttpPost("admin/site-assets")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AdminSiteAssetsViewModel model)
    {
        var content = new SiteAssetContent(model.Css ?? "", model.Js ?? "", model.CssEnabled, model.JsEnabled);
        var result = await assets.SaveAsync(content);

        if (!result.Succeeded)
        {
            return View(IndexView, new AdminSiteAssetsViewModel
            {
                Css = model.Css,
                Js = model.Js,
                CssEnabled = model.CssEnabled,
                JsEnabled = model.JsEnabled,
                Error = result.Error,
            });
        }

        // The stored values themselves are audited by the settings store; the log line records
        // who changed them and what state they were left in, without copying the script into logs.
        logger.LogInformation(
            "Site assets updated by {User}: css {CssState} ({CssLength} chars), js {JsState} ({JsLength} chars)",
            User.Identity?.Name, content.CssEnabled ? "on" : "off", content.Css.Length,
            content.JsEnabled ? "on" : "off", content.Js.Length);

        TempData["SiteAssetsResult"] = "Site CSS and JavaScript saved.";
        return Redirect("/admin/site-assets");
    }
}
