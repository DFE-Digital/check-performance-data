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

    // Persists a single user-submitted feedback message keyed by the visitor's session
    // identifier. The controller ALWAYS reads sessionId server-side (context.Session.Id);
    // a null email means the user chose to hide their address on the form and the value
    // is dropped BEFORE it reaches this method — no encryption, no reveal machinery, so
    // "hidden" here means literally NULL in the row. Returns the auto-assigned id so
    // the caller can build a confirmation URL keyed by it.
    Task<long> CreateAsync(
        string sessionId,
        string whatLookingFor,
        string? whatGot,
        string? email,
        CancellationToken cancellationToken);
}
