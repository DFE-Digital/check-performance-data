namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockDto
{
    public int Id { get; init; }
    public Guid ContentId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string BlockType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? ValueHtml { get; init; }
    public string? LastSeenPath { get; init; }
    public DateTime? LastSeenAt { get; init; }
    public bool AppearInSearch { get; init; } = true;
    public string? Keywords { get; init; }

    /// <summary>Populated only by ContentBlockRepository.SearchAsync — ts_rank of the block's
    /// search vector against the current query. Zero for calls not on the search path.</summary>
    public float Rank { get; init; }

    /// <summary>Per-field ts_rank for the Keywords column (weight A on the ContentBlock vector).
    /// Populated on rows returned by the widened search projection; null on rows built by
    /// non-search code paths.</summary>
    public float? RankKeywords { get; init; }

    /// <summary>Per-field ts_rank for the ValuePlainText column (weight B on the
    /// ContentBlock vector). Populated on rows returned by the widened search projection;
    /// null on rows built by non-search code paths.</summary>
    public float? RankValue { get; init; }

    /// <summary>Null when the row is a kept hit. Non-null when a silent filter dropped the
    /// row: one of the domain slugs applicable to the block corpus — "e2e-key" (fixture key
    /// prefix reserved for E2E seed data), "guidance-ks4-2026-nav-key" (nav-only fixture
    /// block), or "contentblock-appearinsearch-false" (editor toggled the block out of
    /// search). Consumed by telemetry to emit a per-exclusion breadcrumb; user-facing
    /// consumers filter these rows out.</summary>
    public string? ExcludedBy { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
