using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// CMS settings page under the admin shell. Editor-or-admin gated: the admin role implies
// the editor role (Phase 3.2 one-way hierarchy), so gating on the editor role admits both.
// Views live under Views/Admin/Settings so they inherit the admin layout via the
// Views/Admin/_ViewStart cascade, hence the explicit view paths.
[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class AdminSettingsController(ISettingService settings) : Controller
{
    private const string IndexView = "~/Views/Admin/Settings/Index.cshtml";

    [HttpGet("admin/settings")]
    public async Task<IActionResult> Index()
    {
        var items = await settings.GetAllWithValuesAsync();
        return View(IndexView, new AdminSettingsViewModel { Settings = items });
    }

    [HttpPost("admin/settings/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string key, string? value)
    {
        try
        {
            await settings.SaveAsync(key, value);
            TempData["SettingsResult"] = $"Saved {key}.";
        }
        catch (InvalidOperationException)
        {
            // SettingService rejects keys that are not declared in SettingDefinitions.
            TempData["SettingsError"] = $"'{key}' is not a known setting.";
        }

        return Redirect("/admin/settings");
    }
}
