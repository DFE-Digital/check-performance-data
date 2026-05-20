namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// visual regression dashboard ships; until then the landing page renders this as a
// "Coming soon" tile.
public sealed record VisualRegressionNavEntry : IAdminNavEntry
{
    public string Title => "Visual Regression Dashboard";
    public string Description => "Review snapshot diffs and approve/reject baseline changes.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 20;
}
