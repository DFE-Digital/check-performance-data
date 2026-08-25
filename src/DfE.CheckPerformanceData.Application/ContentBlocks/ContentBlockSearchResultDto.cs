namespace DfE.CheckPerformanceData.Application.ContentBlocks;

/// <summary>A content-block match for the help search results page.</summary>
public sealed record ContentBlockSearchResultDto
{
    public required string Key { get; init; }

    /// <summary>The page (and section anchor, where applicable) that renders this block.</summary>
    public required string Url { get; init; }

    public required string PageTitle { get; init; }

    /// <summary>Safe snippet HTML: tag-stripped plain text, the matched term wrapped in &lt;mark&gt;.</summary>
    public required string SnippetHtml { get; init; }

    /// <summary>ts_rank score of the block's search vector. Surfaced in an HTML comment on the search view for debugging.</summary>
    public float Rank { get; init; }

    /// <summary>Per-field ts_rank for the Keywords column (weight A on the ContentBlock vector).
    /// Populated on rows built from the widened search projection; null on rows built by
    /// non-search code paths.</summary>
    public float? RankKeywords { get; init; }

    /// <summary>Per-field ts_rank for the ValuePlainText column (weight B on the ContentBlock
    /// vector). Populated on rows built from the widened search projection; null on rows built
    /// by non-search code paths.</summary>
    public float? RankValue { get; init; }
}
