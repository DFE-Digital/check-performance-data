namespace DfE.CheckPerformanceData.Application.Search;

// Reasons a search query is rejected before hitting the corpus. Empty and too-short
// terms are cheap client-side gates surfaced back to the view so the user sees a
// specific hint rather than an empty result list.
public enum SearchInvalidReason
{
    EmptyQuery,
    BelowMinimumLength,

    // The search backend was unreachable (typically a DbException from the Postgres driver).
    // The Application tier surfaces this as a data value; callers pick their own UX (503 page
    // or inline warning).
    DataStoreUnavailable,
}
