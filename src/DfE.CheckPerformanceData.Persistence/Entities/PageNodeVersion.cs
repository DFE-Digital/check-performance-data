using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Entities;

// A versioned content snapshot for a node. Content is the type-specific payload (widget JSON for
// content nodes, wiki body for wiki nodes). The live version is the one whose publish window
// contains "now"; IsCurrent caches that. Versions above the current one are future/draft.
public sealed class PageNodeVersion
{
    public Guid Id { get; set; }
    public Guid PageNodeId { get; set; }
    public PageNode PageNode { get; set; } = null!;
    public int VersionId { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime? PublishFrom { get; set; }
    public DateTime? PublishTo { get; set; }
    public int MinorVersion { get; set; }

    // The version that replaced this one: the embedded image move adds a new version in place of
    // a live, scheduled or draft version and records it here. A replaced version is never live,
    // scheduled or the working draft again, whatever happens to the new version's window, unless
    // an editor publishes or re-dates it.
    public Guid? SupersededById { get; set; }
    public string Content { get; set; } = string.Empty;
    public string BodyPlainText { get; set; } = string.Empty;

    // Generated tsvector over BodyPlainText at weight D (lower than title/subtitle/keywords
    // on the parent PageNode). SearchPagesAsync ranks live-version + node vectors together so
    // a body hit still contributes to the overall score.
    public NpgsqlTsVector SearchVector { get; set; } = null!;

    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
}
