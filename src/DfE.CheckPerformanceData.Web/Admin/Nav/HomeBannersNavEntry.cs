namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// CMS tile for the start-page banners (#566), between Content blocks (20) and Deleted pages (30).
public sealed record HomeBannersNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.HomeBanners;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Home page banners";
    public string Description => "Add, schedule and order the notification banners shown on the start page.";
    public string Url => "/admin/home-banners";
    public bool Enabled => true;
    public int Order => 25;
}
