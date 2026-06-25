namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class UpdateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }

    // When set, the page is repositioned to this place among its siblings. Used by
    // content-staging import (Replace) to reproduce the source ordering on existing pages.
    public int? SortOrder { get; init; }
}
