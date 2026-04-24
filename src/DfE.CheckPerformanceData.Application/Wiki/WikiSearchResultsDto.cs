namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiSearchResultsDto
{
    public string Query { get; init; } = string.Empty;
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public List<WikiPageSearchResultDto> Items { get; init; } = [];
    public SearchInvalidReason? InvalidReason { get; init; }
}

public enum SearchInvalidReason
{
    EmptyQuery,
    BelowMinimumLength
}
