namespace DfE.CheckPerformanceData.Application.ContentStaging;

// One version of a page node in an export bundle. All versions of a page node are exported so the
// history round-trips, not just the currently-live one. The (PublishFrom, PublishTo) window is the
// source of truth on import — the target environment recomputes which version is live after all
// versions have been re-added. VersionId is preserved to keep the source's history ordering.
public sealed record PageNodeVersionBundleItem
{
    public int VersionId { get; init; }
    public int MinorVersion { get; init; }
    public DateTime? PublishFrom { get; init; }
    public DateTime? PublishTo { get; init; }

    // Type-specific payload: widget-tree JSON for content nodes, wiki body for wiki nodes.
    public string Content { get; init; } = string.Empty;

    // Extracted plain text used for search; carried so the target does not need to re-derive it
    // from Content (which would require knowledge of every content type).
    public string BodyPlainText { get; init; } = string.Empty;
}
