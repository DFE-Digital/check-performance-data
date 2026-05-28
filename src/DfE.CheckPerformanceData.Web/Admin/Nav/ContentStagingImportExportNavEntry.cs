namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Placeholder admin nav entry. Enabled flips to true and Url is populated when the
// content staging import/export feature ships; until then the landing page renders
// this as a "Coming soon" tile under the CMS administration group.
public sealed record ContentStagingImportExportNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ContentStaging;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Content staging import/export";
    public string Description => "Import and export wiki pages and content blocks across environments.";
    public string Url => string.Empty;
    public bool Enabled => false;
    public int Order => 20;
}
