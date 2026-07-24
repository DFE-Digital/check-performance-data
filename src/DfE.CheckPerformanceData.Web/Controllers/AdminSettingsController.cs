using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// System settings page under the admin shell. Gated by the system-settings section grant.
// Views live under Views/Admin/Settings so they inherit the admin layout via the
// Views/Admin/_ViewStart cascade, hence the explicit view paths.
[RequireAdminSection(AdminNavKeys.SystemSettings)]
public sealed class AdminSettingsController(ISettingService settings) : Controller
{
    private const string IndexView = "~/Views/Admin/Settings/Index.cshtml";

    [HttpGet("admin/settings")]
    public async Task<IActionResult> Index(string? sort = null)
    {
        // Query string is the source of truth for the sort direction. Only "key-desc" flips
        // to descending; anything else (null, empty, "key-asc", unrecognised strings) falls
        // back to the ascending default. The URL is user-typeable so malformed values must
        // not throw — silent coercion keeps the page robust.
        var direction = string.Equals(sort, "key-desc", StringComparison.OrdinalIgnoreCase)
            ? SettingSortDirection.KeyDescending
            : SettingSortDirection.KeyAscending;

        var items = await settings.GetAllWithValuesAsync();
        var sorted = direction == SettingSortDirection.KeyDescending
            ? items.OrderByDescending(i => i.Key, StringComparer.OrdinalIgnoreCase).ToList()
            : items.OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase).ToList();

        return View(IndexView, new AdminSettingsViewModel
        {
            Settings = sorted,
            SortDirection = direction,
        });
    }

    [HttpPost("admin/settings/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string key, string? value, string? boolValue = null)
    {
        try
        {
            // Boolean settings render as a checkbox, which only posts a value when ticked.
            // An absent boolValue therefore means unticked, which must persist "false".
            var definition = SettingDefinitions.Find(key);
            var effectiveValue = definition?.Kind == SettingKind.Bool
                ? (string.Equals(boolValue, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false")
                : value;

            await settings.SaveAsync(key, effectiveValue);
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
