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
    IReadOnlyList<SearchEventResultDto> Results,
    // Optional marker set by the sample-data seeder to true. Real events captured from
    // live user requests leave this at its default (false). The sink propagates the
    // flag onto both the parent SearchEvent row and its child SearchEventResult rows so
    // the delete-seeded admin surface can drop either set in isolation.
    bool IsSeeded = false,
    // Optional per-seed-run marker. Non-null only for rows written by the sample-data
    // seeder — the sink writes it through to both the parent and its child result rows
    // so the seed-page Cancel/rollback action can drop this job's rows with a single
    // WHERE job_id = @id predicate across the three sink tables.
    string? JobId = null);
