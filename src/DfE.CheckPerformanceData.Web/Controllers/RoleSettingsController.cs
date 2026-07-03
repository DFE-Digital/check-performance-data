using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Grid of role columns × section rows. Editing this page is gated on the admin role — CMS
// editors should never be able to widen their own access.
[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class RoleSettingsController(IAdminAccessPolicy accessPolicy) : Controller
{
    // Any role that appears in a grant, or the two well-known defaults, becomes a column.
    // Editors can then add a new column by typing a new role name into the "Add role" input.
    private static readonly string[] WellKnownRoles =
    [
        DefaultAdminAccessSeeder.AdminRole,
        DefaultAdminAccessSeeder.EditorRole,
    ];

    [HttpGet("/admin/system/roles")]
    public async Task<IActionResult> Index()
    {
        ViewData["AdminActiveKey"] = Admin.Nav.AdminNavKeys.RoleSettings;
        return View(await BuildViewModelAsync());
    }

    [HttpPost("/admin/system/roles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RoleSettingsFormModel form)
    {
        var userId = User.Identity?.Name;
        var roles = (form.Roles ?? []).Select(r => r?.Trim() ?? string.Empty)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var role in roles)
        {
            var allowedKey = $"grants[{role}]";
            var allowed = form.Grants is not null && form.Grants.TryGetValue(role, out var list)
                ? list ?? []
                : [];
            await accessPolicy.SetGrantsForRoleAsync(role, allowed, userId);
        }

        var newRole = (form.NewRoleName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(newRole)
            && !roles.Any(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase)))
        {
            // Register the new role by writing an empty grant set — no access yet, but the
            // column will appear on the next render so it can be checked in.
            await accessPolicy.SetGrantsForRoleAsync(newRole, [], userId);
        }

        TempData["RoleSettingsSaved"] = "Role access saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<RoleSettingsViewModel> BuildViewModelAsync()
    {
        var grants = await accessPolicy.GetAllGrantsAsync();

        var roles = grants.Select(g => g.RoleName)
            .Concat(WellKnownRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grantSet = grants
            .GroupBy(g => g.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.SectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        return new RoleSettingsViewModel
        {
            Sections = DefaultAdminAccessSeeder.AllSections,
            Roles = roles,
            IsGranted = (role, section) =>
                grantSet.TryGetValue(role, out var s) && s.Contains(section),
            SuccessMessage = TempData["RoleSettingsSaved"] as string,
        };
    }
}
