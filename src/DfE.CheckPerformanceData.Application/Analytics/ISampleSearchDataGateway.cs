namespace DfE.CheckPerformanceData.Application.Analytics;

// Write-only gateway used by SampleSearchDataSeeder for the seed-sample-search-data
// admin page. Separate from ISearchMessageService because that surface intentionally
// server-owns SubmittedAtUtc (user-submitted messages must be stamped at receipt) —
// the seeder needs to back-date rows across the chosen time span. Also exposes the
// destructive maintenance surface (delete seeded / delete all) used by the Danger
// zone on the same admin page — kept on the same interface so a single dev-only
// gateway owns every write path into the sink tables.
public interface ISampleSearchDataGateway
{
    Task WriteBackdatedMessagesAsync(
        IReadOnlyList<BackdatedSearchMessage> messages,
        CancellationToken cancellationToken);

    // Marks-only delete: WHERE is_seeded = true across the three sink tables. Runs
    // in a single transaction so a partial failure cannot leave the parent + child
    // rows in an inconsistent shape. Real rows (is_seeded = false) survive.
    Task<DeleteCountsResult> DeleteSeededAsync(CancellationToken cancellationToken);

    // Scorched-earth delete: every row in the three sink tables regardless of the
    // marker. Wrapped in a transaction for the same reason as DeleteSeededAsync.
    // Used by the Danger zone's "delete all data" typed-confirmation action.
    Task<DeleteCountsResult> DeleteAllAsync(CancellationToken cancellationToken);

    // Per-seed-run rollback: drops rows in the three sink tables whose job_id matches
    // the supplied id. Powered by the Guid marker the seeder stamps on every row it
    // writes. Real user activity (job_id IS NULL) is untouched; other jobs' rows are
    // untouched. Wrapped in a transaction so a partial failure cannot leave the parent
    // and child rows in an inconsistent shape. Used by the seed-page Cancel action:
    // whether the seeder is still running or has just finished, Cancel undoes THIS run.
    Task<DeleteCountsResult> DeleteByJobIdAsync(string jobId, CancellationToken cancellationToken);
}

// Positional record used by the write-gateway. Kept alongside the interface so callers
// see the row shape at the same import. JobId is optional (defaults null): the seeder
// sets it to Guid.ToString("N") of the current seed job id so the rollback path can drop
// every message written by this job with a single WHERE clause.
public sealed record BackdatedSearchMessage(
    string SessionId,
    DateTime SubmittedAtUtc,
    string WhatLookingFor,
    string? WhatGot,
    string? Email,
    string? JobId = null);

// Per-table row counts returned by the destructive maintenance methods. Feeds both
// the TempData success banner ("Deleted N events, M results, K messages") and the
// AuditEntry payload written on each successful delete.
public sealed record DeleteCountsResult(
    int EventsDeleted,
    int ResultsDeleted,
    int MessagesDeleted);
