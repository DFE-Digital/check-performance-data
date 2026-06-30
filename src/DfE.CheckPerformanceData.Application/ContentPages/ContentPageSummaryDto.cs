namespace DfE.CheckPerformanceData.Application.ContentPages;

// A content page as listed on the admin index — enough to identify it and show its state, without
// loading the content tree.
public sealed class ContentPageSummaryDto
{
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string Layout { get; init; }
    public int? PublishedVersionNumber { get; init; }
    public DateTime UpdatedAt { get; init; }

    public bool IsPublished => PublishedVersionNumber is not null;
}
