namespace DfE.CheckPerformanceData.Application.Search;

// Repository-layer projection: the node header plus the currently-live version's plain-text
// body, so the service can build a snippet without loading full JSON content.
public sealed class PageSearchHitRaw
{
    public required Guid PageId { get; init; }
    public required string Path { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string BodyPlainText { get; init; }

    /// <summary>Combined ts_rank score across the PageNode + live-version search vectors.
    /// Zero for calls that don't come through the full-text search path.</summary>
    public float Rank { get; init; }

    /// <summary>Per-field ts_rank for the Keywords column (weight A on the PageNode vector).
    /// Populated when the row was returned by the widened search projection; null when
    /// <see cref="ExcludedBy"/> is non-null the projection may still populate it (draft
    /// pages carry their PageNode ranks because the excluded reason is the missing live
    /// version, not the header fields). Null on rows built by non-search code paths.</summary>
    public float? RankKeywords { get; init; }

    /// <summary>Per-field ts_rank for the Title column (weight B on the PageNode vector).</summary>
    public float? RankTitle { get; init; }

    /// <summary>Per-field ts_rank for the Subtitle column (weight C on the PageNode vector).</summary>
    public float? RankSubtitle { get; init; }

    /// <summary>Per-field ts_rank for the BodyPlainText column on the live PageNodeVersion
    /// (weight D). Null for draft-page-excluded rows (no live version) and for rows built
    /// by non-search code paths.</summary>
    public float? RankBody { get; init; }

    /// <summary>Null when the row is a kept hit. Non-null when a silent filter dropped the
    /// row: one of the domain slugs applicable to the page corpus —
    /// "pagenode-appearinsearch-false" (editor toggled the page out of search) or
    /// "draft-page" (no live version). Consumed by telemetry to emit a per-exclusion
    /// breadcrumb; user-facing consumers filter these rows out.</summary>
    public string? ExcludedBy { get; init; }
}
