namespace DfE.CheckPerformanceData.Application.Wiki;

// A page of deleted-pages results plus the metadata the view needs to render the search
// box, sortable column headers and pager. Sort is one of SortColumn.* and Direction is
// "asc" or "desc" (both normalised by the service).
public sealed class DeletedPagesResult
{
    public IReadOnlyList<DeletedWikiPageDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public string? Search { get; init; }
    public string Sort { get; init; } = DeletedPagesSort.Deleted;
    public string Direction { get; init; } = "desc";
}

// Valid sort keys for the deleted-pages list. Unknown values fall back to Deleted.
public static class DeletedPagesSort
{
    public const string Title = "title";
    public const string Path = "path";
    public const string Deleted = "deleted";
    public const string Children = "children";

    public static string Normalise(string? value) =>
        value is Title or Path or Deleted or Children ? value : Deleted;
}
