namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry for the content-blocks management page: view every block, see which page
// it sits on, and edit them inline one after another.
public sealed record ContentBlocksNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.ContentBlocks;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Content blocks";
    public string Description => "View and edit all content blocks in one place, with the page each one appears on.";
    public string Url => "/admin/content-blocks";
    public bool Enabled => true;
    public int Order => 20;
}
