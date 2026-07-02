namespace DfE.CheckPerformanceData.Application.PageTree;

public sealed class PageNodeVersionDto
{
    public Guid Id { get; init; }
    public int VersionId { get; init; }
    public int MinorVersion { get; init; }
    public bool IsCurrent { get; init; }
    public DateTime? PublishFrom { get; init; }
    public DateTime? PublishTo { get; init; }
    public required string Content { get; init; }

    // Search-friendly plain-text extract of the version's content. Optional on read paths that do
    // not need it; carried on staging export/import so the target does not have to re-derive it.
    public string BodyPlainText { get; init; } = string.Empty;

    public DateTime CreatedDate { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime UpdatedDate { get; init; }
    public string? UpdatedBy { get; init; }
}
