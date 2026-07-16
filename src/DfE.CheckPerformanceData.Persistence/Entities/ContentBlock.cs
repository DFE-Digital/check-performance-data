using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class ContentBlock
{
    public int Id { get; set; }
    // Stable cross-environment identity, preserved through content-staging export/import so the
    // same block is recognised across environments even if its Key changes.
    public Guid ContentId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    // The request path the block was most recently rendered on, recorded automatically by the
    // editable view components. Surfaces "which page this block sits on" for the management page,
    // including blocks placed under dynamically-generated keys that code cannot be scanned for.
    public string? LastSeenPath { get; set; }
    public DateTime? LastSeenAt { get; set; }

    // Whether the block should surface in help-search results. false lets an editor keep a block
    // in place (still rendered on the pages that use it) while suppressing it from /help/search.
    // Default true so existing blocks stay searchable.
    public bool AppearInSearch { get; set; } = true;

    // Editor-provided keywords / search terms — free text. Indexed with weight A (higher than
    // the block's own value/body). Lets an editor pin a block as the top hit for terms that
    // don't appear anywhere in Value.
    public string? Keywords { get; set; }

    // Tag-stripped plaintext of Value, populated by ContentBlockService.SaveAsync via the
    // HTML-rendering service. Feeds the search vector so tsquery matches on words, not on
    // HTML markup like "class" or "govuk-heading-m".
    public string ValuePlainText { get; set; } = string.Empty;

    // Generated tsvector combining Keywords (A) and ValuePlainText (B). Postgres writes this
    // as a stored generated column on insert/update; the app reads but never sets it.
    public NpgsqlTsVector SearchVector { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<ContentBlockVersion> Versions { get; set; } = [];
}
