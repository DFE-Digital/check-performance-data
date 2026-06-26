namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Admin nav entry for the content staging import/export feature, under the CMS
// administration group.
public sealed record ContentStagingImportExportNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ContentStaging;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Content staging import/export";
    public string Description => "Import and export wiki pages and content blocks across environments.";
    public string Url => "/admin/content-staging";
    public bool Enabled => true;
    public int Order => 10;
}
