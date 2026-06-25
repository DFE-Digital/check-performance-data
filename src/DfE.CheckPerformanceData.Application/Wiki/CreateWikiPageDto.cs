namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class CreateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
    public int? ParentId { get; init; }

    // When set, the new page is placed at this position among its siblings instead of being
    // appended to the end. Used by content-staging import to reproduce the source ordering.
    public int? SortOrder { get; init; }
}
