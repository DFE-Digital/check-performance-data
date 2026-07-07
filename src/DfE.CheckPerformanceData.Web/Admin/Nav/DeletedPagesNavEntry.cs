namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry linking to the deleted-pages screen — lives natively under
// /admin so it sits with the other CMS admin tools.
public sealed record DeletedPagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DeletedPages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Deleted pages";
    public string Description => "Review and restore soft-deleted content pages.";
    public string Url => "/admin/pages/deleted";
    public bool Enabled => true;
    public int Order => 30;
}
