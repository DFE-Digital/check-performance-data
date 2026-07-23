namespace DfE.CheckPerformanceData.Application.Search;

// A single page hit for the unified search. Path is stored without a leading slash
// (matching PageNode.Path); the search view is responsible for prefixing "/".
public sealed class PageSearchHitDto
{
    public required Guid PageId { get; init; }
    public required string Path { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string SnippetHtml { get; init; }

    /// <summary>Combined ts_rank score. Surfaced in an HTML comment on the search view for debugging.</summary>
    public float Rank { get; init; }

    /// <summary>Per-field ts_rank for the Keywords column (weight A on the PageNode vector).
    /// Populated on rows built from the widened search projection; null on rows built by
    /// non-search code paths.</summary>
    public float? RankKeywords { get; init; }

    /// <summary>Per-field ts_rank for the Title column (weight B on the PageNode vector).</summary>
    public float? RankTitle { get; init; }

    /// <summary>Per-field ts_rank for the Subtitle column (weight C on the PageNode vector).</summary>
    public float? RankSubtitle { get; init; }

    /// <summary>Per-field ts_rank for the BodyPlainText column on the live PageNodeVersion
    /// (weight D). Null on draft-page rows (no live version) and on rows built by non-search
    /// code paths.</summary>
    public float? RankBody { get; init; }
}
