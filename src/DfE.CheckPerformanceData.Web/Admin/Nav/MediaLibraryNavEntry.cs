namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// CMS tile for the media library, between Content blocks (20) and Home page banners (25).
public sealed record MediaLibraryNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.Media;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Media library";
    public string Description => "Upload images once and reuse them on any page.";
    public string Url => "/admin/media";
    public bool Enabled => true;
    public int Order => 22;
}
