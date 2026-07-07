namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Synthetic IAdminNavEntry created at render time from a PageTreeNode — NOT DI-registered.
// Grafted under the ContentBlocks nav entry so the left-hand tree mirrors the pages tree
// and drilling into a page filters the content-blocks list to just that page's blocks.
public sealed record ContentBlockPageNavEntry(
    Guid PageId,
    string PageTitle,
    string PageType,
    string PagePath,
    bool HasBlocks,
    string ParentNavKey) : IAdminNavEntry
{
    public string Key => $"content-block-page-{PageId}";
    public string? ParentKey => ParentNavKey;
    public string Title => PageTitle;
    public string Description => string.Empty;
    // Filter the flat blocks list to just this page. When the page has no blocks itself
    // (i.e. it's only in the tree because a descendant has blocks), still link so users
    // can see the empty-state and drill deeper.
    public string Url => $"/admin/content-blocks?page=/{PagePath}";
    public bool Enabled => true;
    public int Order => 0; // pre-sorted by the source page tree
}
