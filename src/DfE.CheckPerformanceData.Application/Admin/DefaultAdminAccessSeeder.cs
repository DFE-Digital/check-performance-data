namespace DfE.CheckPerformanceData.Application.Admin;

// Populates the initial role-section-access grid on an empty database:
//   * Admin (cypmd_admin)  →  every well-known section
//   * Editor (cypmd_content_access_user)  →  the CMS-authoring set: content-pages,
//     content-blocks, content-staging, deleted-pages, seed-sample-pages (see below)
// Additional grants added through the settings UI are preserved; the seeder never overwrites
// existing rows.
public sealed class DefaultAdminAccessSeeder(IAdminSectionAccessRepository repository)
{
    public const string AdminRole = "cypmd_admin";
    public const string EditorRole = "cypmd_content_access_user";

    // Every section reachable from the admin left-nav. Kept in sync with AdminNavKeys — anything
    // added there that should be grantable also needs to appear here so the settings page has a
    // row for it. Container-only groups (cms-admin, system-admin, rules-engine-group, messages-group)
    // are omitted because access to a group is implied by access to any of its children — with one
    // exception noted below.
    public static readonly IReadOnlyList<string> AllSections = new[]
    {
        "dashboard",
        "content-pages",
        "content-blocks",
        "deleted-pages",
        "seed-sample-pages",
        "content-staging",
        "system-settings",
        "role-settings",
        "app-logs",
        "rules-config",
        "rules-engine",
        "rules-engine-queue",
        "zendesk-queue",
        "dead-letter-queue",
        "observability",
        "storage-browser",
        "transactions",
        "replay-submissions",
        // Window administration. The nav entries were always registered but no grant existed, so
        // FilterByAccess (which checks each tile's own Key) hid the whole group. manage-window also
        // gates the per-window requests page reached from the windows table.
        "window-admin",
        "new-window",
        "manage-window",
        "reset-seed-data",
        "share-admin",
        "search-analytics",
        "messages-inbox",
        // Exception to the "container-only groups are omitted" rule above. The Test data
        // sub-group's controller uses this key as its [RequireAdminSection] gate so a single
        // admin grant covers every seeder page in the group. Without it a fresh-DB admin
        // would 404 on the group landing / seed-sample-search-data page.
        "test-data-group",
        // Own-key grant for the search-data seed tile so FilterByAccess (which checks each
        // tile's own Key when it has a Url) shows the tile in the sidebar for admins.
        "seed-sample-search-data",
    };

    public async Task SeedIfEmptyAsync()
    {
        var wasEmpty = !await repository.HasAnyAsync();

        // Admin is policy-locked to every section (see RoleSettingsController — the form save
        // always writes AllSections for the admin role, regardless of what was ticked). Top up
        // the DB on every startup so newly-added sections in AllSections become available to
        // admin without waiting for someone to re-save the role settings page.
        var grants = AllSections.Select(s => new RoleSectionAccessGrant(AdminRole, s)).ToList();

        // Editor defaults are user-editable; only apply them on a fresh DB so any subsequent
        // grant changes made from the role settings page stick. The CMS-authoring set covers
        // every surface an editor needs to build the site: pages, blocks, import/export bundles,
        // deleted-page restore, and the sample-seed helper.
        if (wasEmpty)
        {
            grants.Add(new RoleSectionAccessGrant(EditorRole, "content-pages"));
            grants.Add(new RoleSectionAccessGrant(EditorRole, "content-blocks"));
            grants.Add(new RoleSectionAccessGrant(EditorRole, "content-staging"));
            grants.Add(new RoleSectionAccessGrant(EditorRole, "deleted-pages"));
            grants.Add(new RoleSectionAccessGrant(EditorRole, "seed-sample-pages"));
        }

        // Repository SeedAsync is idempotent — skips rows that already exist — so re-running
        // this on a populated DB only inserts the gaps.
        await repository.SeedAsync(grants, "system");
    }
}
