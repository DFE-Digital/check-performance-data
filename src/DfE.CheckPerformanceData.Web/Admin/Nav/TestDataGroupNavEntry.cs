namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Sub-group nested under System administration. Owns the "populate the local database
// with sample data" tiles: Seed sample CMS pages + Seed sample search data. Url is empty
// because the group container is not directly navigable — its children link to their own
// routes. Order 40 places it after Role settings (30) and before Danger zone (top-level, 90).
public sealed record TestDataGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.TestDataGroup;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Test data";
    public string Description => "Populate the local database with sample data for development and demos.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 40;
}
