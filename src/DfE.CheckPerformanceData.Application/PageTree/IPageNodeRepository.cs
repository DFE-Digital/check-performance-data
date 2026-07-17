namespace DfE.CheckPerformanceData.Application.PageTree;

public interface IPageNodeRepository
{
    /// <summary>All live (non-deleted) nodes, ordered by ParentId then SortOrder. Caller builds the tree.</summary>
    Task<List<PageNodeTreeItemDto>> GetTreeAsync();

    /// <summary>
    /// Case-insensitive contains-search across Title, Subtitle and BodyPlainText of the
    /// currently-live version of each non-deleted node. Optional <paramref name="scopePath"/>
    /// (no leading slash) restricts hits to that path or a descendant. Returns raw hits;
    /// snippet building lives in the service layer.
    /// </summary>
    Task<List<Search.PageSearchHitRaw>> SearchPagesAsync(string term, string? scopePath, int max);

    Task<PageNodeDto?> GetByPathAsync(string path);

    /// <summary>
    /// Returns the page at <paramref name="path"/> only if it currently has a live
    /// (IsCurrent = true) version and is not soft-deleted. Returns null otherwise
    /// (unknown path, draft-only, or unpublished). Used by help-search to keep
    /// unpublished pages out of results.
    /// </summary>
    Task<PublishedPageInfoDto?> GetPublishedByPathAsync(string path);

    Task<PageNodeDto?> GetByIdAsync(Guid id);

    Task<PageNodeDto> CreateNodeAsync(
        Guid? parentId, string segment, string path, string title, string pageType, string? userId);

    /// <summary>Adds a new version (max+1) and recomputes IsCurrent. Returns the new VersionId.</summary>
    Task<int> AddVersionAsync(
        Guid nodeId, string content, string bodyPlainText,
        DateTime? publishFrom, DateTime? publishTo, string? userId);

    Task UpdateVersionContentAsync(
        Guid nodeId, int versionId, string content, string bodyPlainText, string? userId);

    /// <summary>Sets the publish window on an existing version. Call RecomputeCurrentAsync after.</summary>
    Task UpdateVersionWindowAsync(
        Guid nodeId, int versionId, DateTime? publishFrom, DateTime? publishTo, string? userId);

    Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId);

    Task<PageNodeVersionDto?> GetLiveVersionAsync(Guid nodeId, DateTime nowUtc);

    /// <summary>Recomputes IsCurrent on all versions: sets it on the resolver's pick, clears others.</summary>
    Task RecomputeCurrentAsync(Guid nodeId, DateTime nowUtc);

    Task<bool> HasChildrenAsync(Guid nodeId);

    Task SoftDeleteAsync(Guid nodeId, string? userId);

    /// <summary>All soft-deleted (DeletedDate != null) nodes ordered by DeletedDate DESC.</summary>
    Task<List<PageNodeDto>> GetDeletedAsync();

    /// <summary>Clears the DeletedDate/DeletedBy on <paramref name="nodeId"/>, restoring the node.</summary>
    Task RestoreAsync(Guid nodeId, string? userId);

    /// <summary>
    /// Swaps the <see cref="PageNodeTreeItemDto.SortOrder"/> values of two nodes in a single
    /// SaveChanges call, so siblings exchange their position in the tree.
    /// </summary>
    Task SwapSortOrderAsync(Guid nodeId, Guid otherNodeId);

    /// <summary>
    /// Reassigns the SortOrder on each listed node in a single transaction, persisting all at once.
    /// All listed nodes should be siblings (same ParentId). Used by MoveAsync to produce a stable
    /// zero-based ordering even when existing SortOrders are all equal.
    /// </summary>
    Task SetSiblingOrderAsync(IReadOnlyList<(Guid Id, int SortOrder)> orders);

    Task ExecuteInTransactionAsync(Func<Task> work);

    /// <summary>
    /// Changes a node's <see cref="PageNodeDto.PageType"/> in place. Used by the default-root
    /// seeder to upgrade legacy folder-typed roots to content pages without recreating them
    /// (which would break any children already parented under them).
    /// </summary>
    Task SetPageTypeAsync(Guid id, string pageType, string? userId);

    /// <summary>
    /// Renames a node's <see cref="PageNodeDto.Segment"/> / <see cref="PageNodeDto.Title"/> and
    /// updates its <see cref="PageNodeDto.Path"/> to <paramref name="newPath"/>. All descendant
    /// nodes whose current Path begins with the old node's Path + '/' have their Path prefixes
    /// rewritten in the same transaction, so the descendant URLs stay valid.
    /// </summary>
    Task RenameNodeAndCascadeAsync(
        Guid id, string newSegment, string newPath, string newTitle, string? newSubtitle, string? newPageName, string? userId);

    /// <summary>
    /// Reparents <paramref name="id"/> under <paramref name="newParentId"/> (null = root),
    /// updates its Path to <paramref name="newPath"/>, sets its SortOrder, and rewrites the
    /// Path of every descendant so the sub-tree's URLs stay valid. All in one transaction.
    /// </summary>
    Task MoveNodeAsync(
        Guid id, Guid? newParentId, string newPath, int newSortOrder, string? userId);

    Task SetShowInMenuAsync(Guid id, bool showInMenu, string? userId);

    /// <summary>Sets the AppearInSearch flag on <paramref name="id"/> and stamps the audit fields.</summary>
    Task SetAppearInSearchAsync(Guid id, bool appearInSearch, string? userId);

    /// <summary>Sets the free-text search Keywords on <paramref name="id"/> and stamps the audit fields.</summary>
    Task SetKeywordsAsync(Guid id, string? keywords, string? userId);

    /// <summary>
    /// Clones a page as a sibling of the source: a fresh Guid, a segment/path derived from
    /// <paramref name="newSegment"/> under the source's parent, every version copied verbatim
    /// (content + PublishFrom/To + IsCurrent + MinorVersion), and SortOrder placed at the tail of
    /// the sibling set. All in one transaction so the copy is either fully written or not at all.
    /// Returns <c>null</c> if the source id is unknown or already soft-deleted.
    /// </summary>
    Task<PageNodeDto?> CopyNodeAsync(Guid sourceId, string newSegment, string newTitle, string? userId);

    // ── Staging (import) — explicit-id creates, used only by ContentStagingService ─────────
    //
    // These preserve the exporter's identities so a bundle round-trips faithfully: the node's Id
    // and each version's VersionId / MinorVersion / IsCurrent / publish window are set directly
    // from the bundle rather than auto-assigned. They are not intended for interactive editing.

    /// <summary>
    /// Creates a node with an explicit <paramref name="id"/> and an explicit
    /// <paramref name="sortOrder"/>, so a bundle's identity and ordering survive round-trip.
    /// The caller supplies the materialised <paramref name="path"/> (segment or parent.Path + '/' +
    /// segment).
    /// </summary>
    Task<PageNodeDto> CreateNodeForStagingAsync(
        Guid id, Guid? parentId, string segment, string path,
        string title, string? subtitle, string? pageName, string pageType, int sortOrder,
        bool appearInSearch, string? keywords, string? userId);

    /// <summary>
    /// Updates an existing node's mutable header fields (segment, title, subtitle, path, sortOrder).
    /// Used on import when a node with the bundle's Id already exists and the collision decision is
    /// Overwrite. Does not touch versions — those are handled by
    /// <see cref="ReplaceAllVersionsForStagingAsync"/>.
    /// </summary>
    Task UpdateNodeForStagingAsync(
        Guid id, string segment, string path, string title, string? subtitle, string? pageName, int sortOrder,
        bool appearInSearch, string? keywords, string? userId);

    /// <summary>
    /// Replaces every version on <paramref name="nodeId"/> with the supplied bundle versions.
    /// Existing versions are deleted; the bundle versions are added with their original VersionId
    /// / MinorVersion / windows. IsCurrent is recomputed at the end from the (PublishFrom,
    /// PublishTo) windows relative to <c>DateTime.UtcNow</c>, so the target picks a live version
    /// consistently even if the source's clock differed.
    /// </summary>
    Task ReplaceAllVersionsForStagingAsync(
        Guid nodeId, IReadOnlyList<PageNodeVersionDto> versions, string? userId);

    /// <summary>
    /// Wipes every page node, page-node version, content block, and content-block version in a
    /// single TRUNCATE ... CASCADE. Powers the admin-only "Clear all CMS content" button on the
    /// content-staging page: gives editors/QA a reliable "reset to empty" state before importing
    /// a bundle, without shell access to the database. Identity sequences are reset so the next
    /// insert starts at 1.
    /// </summary>
    Task TruncateAllContentAsync();
}
