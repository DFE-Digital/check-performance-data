namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry for the content page builder: list every content page, see its published or
// draft state, and create or edit one.
public sealed record ContentPagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ContentPages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Pages";
    public string Description => "Browse and manage all pages in the site tree — content, wiki, and folder nodes.";
    public string Url => "/admin/pages";
    public bool Enabled => true;
    public int Order => 10;
}
