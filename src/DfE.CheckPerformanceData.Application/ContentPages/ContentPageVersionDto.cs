namespace DfE.CheckPerformanceData.Application.ContentPages;

// A published snapshot of a content page. Snapshot is the serialised content tree captured when the
// version was published.
public sealed class ContentPageVersionDto
{
    public int Id { get; init; }
    public int VersionNumber { get; init; }
    public required string Snapshot { get; init; }
    public required string Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
