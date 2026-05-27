namespace DfE.CheckPerformanceData.Application.Wiki;

// Search/sort/paging parameters for the deleted-pages list. PageSize is supplied by the
// caller (resolved from the Wiki:PageLength setting); the rest come from the request.
public sealed record DeletedPagesQuery
{
    public string? Search { get; init; }
    public string? Sort { get; init; }
    public string? Direction { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
