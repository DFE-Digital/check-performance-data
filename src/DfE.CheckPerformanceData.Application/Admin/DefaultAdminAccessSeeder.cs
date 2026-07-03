namespace DfE.CheckPerformanceData.Application.Admin;

// Populates the initial role-section-access grid on an empty database:
//   * Admin (cypmd_admin)  →  every well-known section
//   * Editor (cypmd_content_access_user)  →  content-pages + seed-sample-pages only
// Additional grants added through the settings UI are preserved; the seeder never overwrites
// existing rows.
public sealed class DefaultAdminAccessSeeder(IAdminSectionAccessRepository repository)
{
    public const string AdminRole = "cypmd_admin";
    public const string EditorRole = "cypmd_content_access_user";

    // Every section reachable from the admin left-nav. Kept in sync with AdminNavKeys — anything
    // added there that should be grantable also needs to appear here so the settings page has a
    // row for it. Container-only groups (cms-admin, system-admin, rules-engine-group) are omitted
    // because access to a group is implied by access to any of its children.
    public static readonly IReadOnlyList<string> AllSections = new[]
    {
        "content-pages",
        "content-blocks",
        "deleted-pages",
        "seed-sample-pages",
        "content-staging",
        "system-settings",
        "role-settings",
        "rules-config",
        "rules-engine",
        "rules-engine-queue",
        "zendesk-queue",
        "dead-letter-queue",
        "observability",
        "storage-admin",
        "storage-browser",
        "transactions",
        "replay-submissions",
        "amendment-requests-admin",
        "uncommitted-requests",
        "reset-seed-data",
    };

    public async Task SeedIfEmptyAsync()
    {
        if (await repository.HasAnyAsync()) return;

        var grants = new List<RoleSectionAccessGrant>();
        foreach (var section in AllSections)
            grants.Add(new RoleSectionAccessGrant(AdminRole, section));

        // Editor default: content pages + the seed-sample-pages helper only.
        grants.Add(new RoleSectionAccessGrant(EditorRole, "content-pages"));
        grants.Add(new RoleSectionAccessGrant(EditorRole, "seed-sample-pages"));

        await repository.SeedAsync(grants, "system");
    }
}
