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

    // Sentinel section written for every registered role so the row persists in the DB even
    // when every real section is unchecked. That's how the "Register a new role" flow puts a
    // fresh column into the grid before any grants have been made.
    public const string RegisteredMarker = "__registered__";

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
            List<string> allowed;
            if (string.Equals(role, DefaultAdminAccessSeeder.AdminRole, StringComparison.OrdinalIgnoreCase))
            {
                // Admin is locked to every section server-side, whatever the form contained.
                allowed = DefaultAdminAccessSeeder.AllSections.ToList();
            }
            else
            {
                allowed = form.Grants is not null && form.Grants.TryGetValue(role, out var list)
                    ? list?.ToList() ?? []
                    : [];
            }
            // Sentinel so the role's column persists even when every real section is unchecked.
            allowed.Add(RegisteredMarker);
            await accessPolicy.SetGrantsForRoleAsync(role, allowed, userId);
        }

        var newRole = (form.NewRoleName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(newRole)
            && !roles.Any(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase)))
        {
            // Register the new role by writing just the sentinel — no real grants yet, but the
            // column will appear on the next render so it can be ticked in.
            await accessPolicy.SetGrantsForRoleAsync(newRole, [RegisteredMarker], userId);
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
            .Where(g => g.SectionKey != RegisteredMarker)
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
