namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry linking to the editor-accessible Deleted Pages screen. Admins
// reach /help/deleted directly from the landing tile (no /admin/deleted-pages wrapper);
// editors still use the existing Help-area link.
public sealed record DeletedPagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.DeletedPages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Deleted pages";
    public string Description => "Review, restore or permanently delete pages removed from the wiki.";
    public string Url => "/help/deleted";
    public bool Enabled => true;
    public int Order => 20;
}
