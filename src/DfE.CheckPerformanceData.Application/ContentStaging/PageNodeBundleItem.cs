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
    private readonly string _segment = string.Empty;
    public string Segment { get => _segment; init => _segment = BundleMemberDefaults.OrEmpty(value); }
    private readonly string _title = string.Empty;
    public string Title { get => _title; init => _title = BundleMemberDefaults.OrEmpty(value); }

    /// <summary>Optional lede rendered above the page H1. Null when unset.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Optional short label used in nav/tree/list surfaces. Null when unset.</summary>
    public string? PageName { get; init; }

    private readonly string _pageType = "folder";
    public string PageType { get => _pageType; init => _pageType = BundleMemberDefaults.OrEmpty(value); }
    public int SortOrder { get; init; }

    /// <summary>
    /// Whether the page appears in help-search. Defaults to true so bundles produced by earlier
    /// exporters (without this field) round-trip as searchable, matching the DB default.
    /// </summary>
    public bool AppearInSearch { get; init; } = true;

    /// <summary>
    /// Free-text search keywords. Nullable — older bundles omit this field and it round-trips
    /// as null (matching the DB default). Weighted highest in the search index at import time.
    /// </summary>
    public string? Keywords { get; init; }

    private readonly List<PageNodeVersionBundleItem> _versions = [];
    public List<PageNodeVersionBundleItem> Versions
    {
        get => _versions;
        init => _versions = BundleMemberDefaults.NonNullItems(value);
    }
}
