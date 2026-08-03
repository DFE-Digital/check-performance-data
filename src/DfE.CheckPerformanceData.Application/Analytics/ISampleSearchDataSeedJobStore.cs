using System.Collections.Concurrent;

namespace DfE.CheckPerformanceData.Application.Analytics;

// In-memory job store for the seed-sample-search-data admin page. Owns a thread-safe
// dictionary of in-flight + recently-completed jobs so the client-side progress modal can
// poll for a live snapshot without touching Postgres. Singleton — a job started on one
// request must be readable on a subsequent poll request.
//
// Lifecycle:
//   * Register — controller POST creates a new job, hands the store a cancellation source,
//     and kicks the seeder onto a background task. Store returns the JobId used in the
//     redirect Location + subsequent progress polls.
//   * Update — the seeder's IProgress<T> callback funnels each tick into the store, which
//     mutates the tracked job in place. UpdateProgress is safe to call from a background
//     thread.
//   * Complete / Fail — the background task calls one of these when the seed finishes so
//     the poll GET can transition the UI into the success or failure state.
//   * Cancel — the modal's Cancel button posts to the DELETE endpoint which asks the store
//     to trigger the tracked CancellationTokenSource. The seeder respects the token between
//     batches and throws OperationCanceledException; the background task catches it and
//     calls MarkCancelled to record the "cancelled at N events" state.
//
// Completed jobs are evicted on new-job insert once they exceed the retention window (5
// minutes) — small enough that a reload of the progress page shows stale-then-cleared,
// large enough that the modal has time to render the completion state after the last poll.
public interface ISampleSearchDataSeedJobStore
{
    // Registers a new job and returns its JobId. Callers use the JobId in the redirect
    // Location + subsequent progress polls. The store owns the CancellationTokenSource so
    // Cancel can trigger it from a different request.
    SampleSearchDataSeedJob Register(
        string presetLabel,
        int eventsTotal,
        int messagesTotal,
        CancellationTokenSource cancellationSource);

    // Reads a snapshot of the job by id. Returns null when the id is unknown (either it
    // was never created or it has been evicted after the retention window).
    SampleSearchDataSeedJob? Get(Guid jobId);

    // Applies a progress tick to the tracked job. Safe from any thread; no-op when the
    // id is unknown (which can happen if the job was cancelled and evicted between the
    // seeder's tick emission and this method actually acquiring the entry).
    void UpdateProgress(Guid jobId, SampleSearchDataSeedProgressTick tick);

    // Terminal transition when the seed finishes normally. Persists the final counts +
    // the AuditEntry id so the modal can link back to the audit trail.
    void MarkCompleted(Guid jobId, int auditEntryId, string? note = null);

    // Terminal transition when the seed throws. Records the exception message so the
    // modal can render a Retry button + the reason.
    void MarkFailed(Guid jobId, string errorMessage);

    // Intermediate transition: mark a Running job as Cancelling so the poll response
    // exposes the "cancelling…" state to the JS. Safe to call on any state (unknown /
    // terminal states are no-ops). Distinct from RequestCancel because the controller's
    // Cancel-then-rollback flow signals the token, then waits for the seeder to unwind
    // to a terminal state before invoking rollback — during that unwind window the UI
    // must render "cancelling" so a second click on Cancel does not fire another
    // rollback.
    void MarkCancelling(Guid jobId);

    // Signals the tracked CancellationTokenSource when the job is still running so the
    // seeder unwinds via OperationCanceledException, then transitions to the Cancelling
    // intermediate state. Returns true when the id is known — the controller uses the
    // return value to decide whether to proceed with rollback of the job's rows, NOT
    // whether the seeder was actually in flight. Cancel of an already-completed job is
    // therefore a valid transition: it lets the admin roll back a finished-too-quickly
    // seed run.
    bool RequestCancel(Guid jobId);
}

public enum SampleSearchDataSeedJobState
{
    Running,
    // Intermediate state entered when Cancel has fired the token but the seeder has not
    // yet unwound to a terminal state. The JS polls this so the modal can render a
    // "Cancelling…" label + disable the Cancel button; the controller uses the state
    // to know it has finished waiting for the seeder before invoking the row rollback.
    Cancelling,
    Completed,
    Failed,
}

// Snapshot of a job as consumed by the progress-poll endpoint. All fields are read-only —
// the store mutates its internal copy and returns a fresh record on each Get.
public sealed record SampleSearchDataSeedJob(
    Guid JobId,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int EventsWritten,
    int EventsTotal,
    int MessagesWritten,
    int MessagesTotal,
    DateTime? CurrentCursorUtc,
    SampleSearchDataSeedJobState State,
    string? ErrorMessage,
    string PresetLabel,
    int AuditEntryId,
    string? Note);

public sealed class InMemorySampleSearchDataSeedJobStore : ISampleSearchDataSeedJobStore
{
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, Entry> _jobs = new();
    private readonly TimeProvider _clock;

    public InMemorySampleSearchDataSeedJobStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public SampleSearchDataSeedJob Register(
        string presetLabel,
        int eventsTotal,
        int messagesTotal,
        CancellationTokenSource cancellationSource)
    {
        EvictExpired();

        var jobId = Guid.NewGuid();
        var entry = new Entry(cancellationSource)
        {
            JobId = jobId,
            StartedAtUtc = _clock.GetUtcNow().UtcDateTime,
            EventsTotal = eventsTotal,
            MessagesTotal = messagesTotal,
            PresetLabel = presetLabel,
            State = SampleSearchDataSeedJobState.Running,
        };
        _jobs[jobId] = entry;
        return entry.Snapshot();
    }

    public SampleSearchDataSeedJob? Get(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var entry) ? entry.Snapshot() : null;
    }

    public void UpdateProgress(Guid jobId, SampleSearchDataSeedProgressTick tick)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return;
        }
        lock (entry.Sync)
        {
            // Never let a later tick's counter go backwards — Progress<T> may deliver ticks
            // out of order across sync-context posts. Take the max.
            entry.EventsWritten = Math.Max(entry.EventsWritten, tick.EventsWritten);
            entry.MessagesWritten = Math.Max(entry.MessagesWritten, tick.MessagesWritten);
            entry.CurrentCursorUtc = tick.CurrentCursorUtc;
        }
    }

    public void MarkCompleted(Guid jobId, int auditEntryId, string? note = null)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return;
        }
        lock (entry.Sync)
        {
            entry.State = SampleSearchDataSeedJobState.Completed;
            entry.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
            entry.AuditEntryId = auditEntryId;
            entry.Note = note;
        }
    }

    public void MarkFailed(Guid jobId, string errorMessage)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return;
        }
        lock (entry.Sync)
        {
            entry.State = SampleSearchDataSeedJobState.Failed;
            entry.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
            entry.ErrorMessage = errorMessage;
        }
    }

    public bool RequestCancel(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return false;
        }
        // Only signal the token if the seeder is still running. If the job already
        // reached a terminal (Completed / Failed) state — most 24 h-preset seeds finish
        // in ~3 s and beat the user's click — the return-true handoff below still lets
        // the controller proceed with row rollback. This is the "Cancel = interrupt +
        // rollback" contract: the rollback undoes THIS run's rows even if the seeder
        // itself finished before the button landed.
        if (entry.State == SampleSearchDataSeedJobState.Running)
        {
            lock (entry.Sync)
            {
                entry.State = SampleSearchDataSeedJobState.Cancelling;
            }
            try
            {
                entry.CancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Race window: the seeder's finally block disposed the CTS on the same
                // thread that transitioned Running → Completed. Not fatal — the row-
                // rollback still runs; return true so the caller proceeds.
            }
        }
        return true;
    }

    public void MarkCancelling(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return;
        }
        lock (entry.Sync)
        {
            // Only Running → Cancelling is a legal transition; a terminal-state job
            // stays where it is.
            if (entry.State == SampleSearchDataSeedJobState.Running)
            {
                entry.State = SampleSearchDataSeedJobState.Cancelling;
            }
        }
    }

    // Evict completed / failed jobs that have aged past the retention window. Called on
    // every Register so the store never grows unbounded — the alternative (background
    // timer) would need disposal wiring and adds no material precision.
    private void EvictExpired()
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime - CompletedRetention;
        foreach (var kvp in _jobs)
        {
            if (kvp.Value.State != SampleSearchDataSeedJobState.Running
                && kvp.Value.CompletedAtUtc.HasValue
                && kvp.Value.CompletedAtUtc.Value < cutoff)
            {
                _jobs.TryRemove(kvp.Key, out _);
            }
        }
    }

    // Mutable inner record — the public snapshot type is immutable. Holds its own sync-lock
    // so multi-threaded UpdateProgress + MarkCompleted calls do not tear a snapshot.
    private sealed class Entry
    {
        public Entry(CancellationTokenSource cancellationSource)
        {
            CancellationSource = cancellationSource;
        }

        public object Sync { get; } = new();

        public Guid JobId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public int EventsWritten { get; set; }
        public int EventsTotal { get; set; }
        public int MessagesWritten { get; set; }
        public int MessagesTotal { get; set; }
        public DateTime? CurrentCursorUtc { get; set; }
        public SampleSearchDataSeedJobState State { get; set; }
        public string? ErrorMessage { get; set; }
        public string PresetLabel { get; set; } = string.Empty;
        public int AuditEntryId { get; set; }
        public string? Note { get; set; }
        public CancellationTokenSource CancellationSource { get; }

        public SampleSearchDataSeedJob Snapshot()
        {
            lock (Sync)
            {
                return new SampleSearchDataSeedJob(
                    JobId,
                    StartedAtUtc,
                    CompletedAtUtc,
                    EventsWritten,
                    EventsTotal,
                    MessagesWritten,
                    MessagesTotal,
                    CurrentCursorUtc,
                    State,
                    ErrorMessage,
                    PresetLabel,
                    AuditEntryId,
                    Note);
            }
        }
    }
}
