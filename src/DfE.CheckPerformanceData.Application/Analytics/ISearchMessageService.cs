namespace DfE.CheckPerformanceData.Application.Analytics;

// Message-service surface — messages-only. The single source of truth for search_messages
// writes and deletes.
//
// Seed method here is PurgeExpiredMessagesAsync so the retention job compiles against a
// stable surface from the moment the storage-foundation plan lands. Later plans extend
// this interface:
//   - the emission plan fills PurgeExpiredMessagesAsync with the real batched delete
//   - the feedback-form plan adds CreateAsync
//   - the admin inbox plan adds inbox reads, mark-read, and per-session purge
//
// Do NOT add a messages-purge method to ISearchAnalyticsSink. Two delete paths to the
// same table via two different owners is exactly the split-brain the two-interface
// boundary exists to prevent.
public interface ISearchMessageService
{
    Task<int> PurgeExpiredMessagesAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}
