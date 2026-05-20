namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// version-retention feature ships; until then the landing page renders this as a
// "Coming soon" tile.
public sealed record VersionRetentionNavEntry : IAdminNavEntry
{
    public string Title => "Version retention";
    public string Description => "Cap version history and permanently delete pruned versions.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 10;
}
