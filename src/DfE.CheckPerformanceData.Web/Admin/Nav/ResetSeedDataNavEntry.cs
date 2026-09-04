namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Danger zone tile that links (GET) to the "Reset seed data" interstitial confirmation page.
// HttpMethod is GET because the tile leads to a warning page, not the destructive action
// itself — the reset only runs from the confirmation page's POST. This tile alone carries the
// Production gate (includeResetSeedData in AddAdminNavEntries); the group around it does not.
public sealed record ResetSeedDataNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ResetSeedData;
    public string? ParentKey => AdminNavKeys.DangerZone;
    public string Title => "Reset seed data";
    public string Description => "Wipe and reseed the database, pupil data and question flows back to the default seeded state. Destroys all tester data.";
    public string Url => "/admin/danger-zone/reset-seed-data";
    public bool Enabled => true;
    public int Order => 10;
}
