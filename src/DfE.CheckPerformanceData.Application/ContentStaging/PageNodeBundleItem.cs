namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single page node in an export bundle. Identity and parentage are carried purely by stable
// GUIDs: Id is the node's cross-environment identity, ParentId its parent's (null for a root). The
// Segment is the URL slug segment carried so a custom (SEO-pinned) segment survives round-trip; it
// is NOT used to match nodes. The materialised Path is rebuilt on import by walking the ParentId
// chain, never carried in the bundle (would go stale under moves). PageType is folder | content |
// wiki; folders never have versions, content and wiki nodes carry their full version history.
public sealed record PageNodeBundleItem
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Segment { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional lede rendered above the page H1. Null when unset.</summary>
    public string? Subtitle { get; init; }

    public string PageType { get; init; } = "folder";
    public int SortOrder { get; init; }
    public List<PageNodeVersionBundleItem> Versions { get; init; } = [];
}
