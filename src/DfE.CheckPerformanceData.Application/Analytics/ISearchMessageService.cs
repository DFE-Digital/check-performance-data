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

    // Cheap COUNT(*) over unread messages. Feeds the admin-chrome badge, so it runs on
    // every admin page load — the ix_search_messages_is_read partial index keeps the
    // scan bounded regardless of table size.
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken);

    // One page of the inbox, ordered by the caller-selected sort. filterText matches on
    // the first-line preview substring (case-insensitive contains on what_looking_for).
    // TotalCount reflects the filtered set, not the whole table, so the paginator renders
    // pages against the filter the admin sees.
    Task<PagedResult<SearchMessageSummary>> GetInboxAsync(
        SearchMessageSortField sortField,
        bool sortDesc,
        string? filterText,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    // Full detail for the /admin/Messages/Inbox/{id} detail view. Returns null when no
    // row exists so the controller can 404 without an extra existence probe.
    Task<SearchMessageDetail?> GetByIdAsync(long id, CancellationToken cancellationToken);

    // Idempotent global mark-read: stamps is_read + read_by_admin_sub + read_at_utc on
    // the row iff it is not already read. Re-invoking on an already-read row is a
    // zero-row update — the WHERE clause filters is_read = false, so any second admin's
    // click leaves the first admin's attribution intact.
    Task MarkReadAsync(
        long id,
        string readByAdminSub,
        DateTime readAtUtc,
        CancellationToken cancellationToken);

    // Deletes every message for the session. Returns the deleted count so the caller
    // (session-purge action) can record it in its audit payload alongside the events +
    // results counts it deletes separately in the same transaction.
    Task<SessionPurgeCounts> PurgeSessionAsync(string sessionId, CancellationToken cancellationToken);

    // All messages for the session, newest first. Used by the session drill-in so the
    // admin can jump straight from a session's search history to the messages the same
    // session filed.
    Task<IReadOnlyList<SearchMessageSummary>> GetForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);
}
