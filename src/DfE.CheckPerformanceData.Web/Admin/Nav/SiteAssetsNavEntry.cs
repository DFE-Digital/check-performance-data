namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile linking to the site-wide CSS and JavaScript page (/admin/site-assets),
// in the CMS administration group.
public sealed record SiteAssetsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SiteAssets;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Site CSS and JavaScript";
    public string Description => "Add custom CSS and JavaScript that applies to every public page. CMS administrators only.";
    public string Url => "/admin/site-assets";
    public bool Enabled => true;
    public int Order => 70;
}
