namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Group descriptor: top-level "Danger zone" section for destructive operational actions.
// ParentKey is null so the landing-page controller treats it as a group container. The group
// itself is registered everywhere; each tile decides its own environments (Reset seed data is
// registered only outside Production, the blob storage browser everywhere), and an empty group
// is dropped by FilterByAccess rather than rendering as a bare heading.
// High Order so it sorts last, after the routine admin groups.
public sealed record DangerZoneGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DangerZone;
    public string? ParentKey => null;
    public string Title => "Danger zone";
    public string Description => "Destructive operations. These permanently delete data — use with care.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 90;
}
