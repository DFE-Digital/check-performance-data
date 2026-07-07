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
}
