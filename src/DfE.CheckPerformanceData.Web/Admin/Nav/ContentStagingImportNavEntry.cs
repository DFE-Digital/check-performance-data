namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// content staging import feature ships; until then the landing page renders this as a
// "Coming soon" tile.
public sealed record ContentStagingImportNavEntry : IAdminNavEntry
{
    public string Title => "Content Staging Import";
    public string Description => "Import wiki pages and content blocks exported from another environment.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 30;
}
