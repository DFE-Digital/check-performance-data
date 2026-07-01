namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Synthetic IAdminNavEntry created at render time from a PageTreeNode — NOT DI-registered.
// Grafted into the admin nav forest so the recursive node partial can render page-tree
// nodes using the same tv-* CSS/JS as any other admin nav entry.
public sealed record PageTreeNavEntry(
    Guid PageId,
    string PageTitle,
    string PageType,
    bool HasLiveVersion,
    string ParentNavKey) : IAdminNavEntry
{
    public string Key => $"page-{PageId}";
    public string? ParentKey => ParentNavKey;
    public string Title => PageTitle;
    public string Description => string.Empty;
    public string Url => $"/admin/pages/{PageId}";
    public bool Enabled => true;
    public int Order => 0; // pre-sorted by PageTreeBuilder
    public string HttpMethod => "GET";
}
