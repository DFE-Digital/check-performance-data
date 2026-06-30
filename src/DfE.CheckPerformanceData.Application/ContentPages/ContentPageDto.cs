namespace DfE.CheckPerformanceData.Application.ContentPages;

// A content page as seen by the application layer: the editable draft tree (serialised JSON) plus the
// number of the version the public route renders (null until first published).
public sealed class ContentPageDto
{
    public int Id { get; init; }
    public Guid ContentId { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Caption { get; init; }
    public required string Layout { get; init; }
    public required string DraftContent { get; init; }
    public int? PublishedVersionNumber { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
