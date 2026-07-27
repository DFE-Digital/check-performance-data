namespace DfE.CheckPerformanceData.Application.Analytics;

// Read-side projections over search_messages. The current feedback-form work only writes
// messages, so no read shapes live here yet — the admin messages inbox follow-up extends
// this file with SearchMessageSummary (inbox row), SearchMessageDetail (detail view),
// PagedResult<T> (inbox paging), SearchMessageSortField (sortable columns), and
// SessionPurgeCounts (per-session purge return shape). Kept as a namespace stub so the
// follow-up plan has a stable file to extend without a rename.
