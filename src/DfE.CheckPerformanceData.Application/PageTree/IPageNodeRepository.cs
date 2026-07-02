namespace DfE.CheckPerformanceData.Application.PageTree;

public interface IPageNodeRepository
{
    /// <summary>All live (non-deleted) nodes, ordered by ParentId then SortOrder. Caller builds the tree.</summary>
    Task<List<PageNodeTreeItemDto>> GetTreeAsync();

    Task<PageNodeDto?> GetByPathAsync(string path);

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
        Guid id, string newSegment, string newPath, string newTitle, string? newSubtitle, string? userId);

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
        string title, string? subtitle, string pageType, int sortOrder, string? userId);

    /// <summary>
    /// Updates an existing node's mutable header fields (segment, title, subtitle, path, sortOrder).
    /// Used on import when a node with the bundle's Id already exists and the collision decision is
    /// Overwrite. Does not touch versions — those are handled by
    /// <see cref="ReplaceAllVersionsForStagingAsync"/>.
    /// </summary>
    Task UpdateNodeForStagingAsync(
        Guid id, string segment, string path, string title, string? subtitle, int sortOrder, string? userId);

    /// <summary>
    /// Replaces every version on <paramref name="nodeId"/> with the supplied bundle versions.
    /// Existing versions are deleted; the bundle versions are added with their original VersionId
    /// / MinorVersion / windows. IsCurrent is recomputed at the end from the (PublishFrom,
    /// PublishTo) windows relative to <c>DateTime.UtcNow</c>, so the target picks a live version
    /// consistently even if the source's clock differed.
    /// </summary>
    Task ReplaceAllVersionsForStagingAsync(
        Guid nodeId, IReadOnlyList<PageNodeVersionDto> versions, string? userId);
}
