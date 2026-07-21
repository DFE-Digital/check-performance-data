namespace DfE.CheckPerformanceData.Application.Search;

// Reasons a search query is rejected before hitting the corpus. Empty and too-short
// terms are cheap client-side gates surfaced back to the view so the user sees a
// specific hint rather than an empty result list.
public enum SearchInvalidReason
{
    EmptyQuery,
    BelowMinimumLength,
}
