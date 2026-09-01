namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Cross-pod mutual exclusion for content-staging Import. Two admins hitting Import
// simultaneously (e.g. one from each of two pods behind the AKS ingress) would race
// on individual pages and produce a chaotic mixed outcome — both would see success
// banners, but the resulting page tree would be a non-deterministic merge of the
// two bundles. The advisory lock forces a single-runner-at-a-time posture: whichever
// admin's request arrives first acquires the lock; the second gets a specific
// "another import is in progress" error rather than an obscure conflict/violation.
//
// Postgres advisory locks are session-scoped, so the Release call is mandatory (a
// crashed pod would drop its session and the lock would release with it; a live
// pod that forgets to release leaks the lock until the connection returns to pool
// and is recycled). Callers must Release in a finally block.
public interface IContentStagingLock
{
    // Non-blocking try-acquire. Returns true if this caller now holds the lock and
    // must Release it; false if another caller holds it.
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);

    // Releases the lock previously acquired by TryAcquireAsync on this instance.
    // Safe to call even if the acquire returned false (pg_advisory_unlock returns
    // false silently in that case).
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
