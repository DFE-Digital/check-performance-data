namespace DfE.CheckPerformanceData.Application.Logging;

// Filter/paging inputs for the admin logs page. Every field is optional; the repository
// applies each as an AND-filter and short-circuits when the field is null/empty. Timestamp
// ordering is always DESC — the "most recent first" default the admin UI expects.
public sealed record AppLogQuery(
    string? Level = null,
    string? Category = null,
    string? Search = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Skip = 0,
    int Take = 50);

public sealed record AppLogPage(
    IReadOnlyList<AppLogDto> Rows,
    int Total,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Levels);
