namespace DfE.CheckPerformanceData.Application.Analytics;

// The write-side payload the search-analytics sink hands to Postgres. Positional record
// so consumers can construct one line and the sink writer never diverges from the
// column set the underlying entity carries. results_total and zero_results are omitted
// because Postgres computes them from ResultsPages + ResultsBlocks — the writer sets
// only the raw inputs.
public sealed record SearchEventDto(
    DateTime OccurredAtUtc,
    string SessionId,
    string? QueryRaw,
    string? QueryNormalised,
    string? Scope,
    int ResultsPages,
    int ResultsBlocks,
    int LatencyMs,
    IReadOnlyList<SearchEventResultDto> Results);
