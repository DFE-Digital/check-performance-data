namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// visual regression dashboard ships; until then the landing page renders this as a
// "Coming soon" tile under the System administration group.
public sealed record VisualRegressionNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.VrDashboard;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Visual regression dashboard";
    public string Description => "Review snapshot diffs and approve or reject baseline changes.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 10;
}
