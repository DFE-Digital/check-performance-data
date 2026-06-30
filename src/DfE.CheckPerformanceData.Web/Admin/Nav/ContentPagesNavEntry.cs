namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry for the content page builder: list every content page, see its published or
// draft state, and create or edit one.
public sealed record ContentPagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ContentPages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Content pages";
    public string Description => "Build and edit content pages from reusable regions and widgets, then publish them.";
    public string Url => "/admin/content-pages";
    public bool Enabled => true;
    public int Order => 35;
}
