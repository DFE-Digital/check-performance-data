namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#302158: cross-pod mutual exclusion for the automatic exercise hand-over. Every web pod runs
/// the job; whichever takes this lock sweeps, and the others skip that tick.
/// </summary>
/// <remarks>
/// Session-scoped, like <c>IContentStagingLock</c>: a pod that dies drops its session and the lock
/// with it, and a live holder must release in a finally block.
/// </remarks>
public interface IExerciseHandOverLock
{
    /// <summary>Non-blocking. True when this caller now holds the lock and must release it.</summary>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases a lock taken by <see cref="TryAcquireAsync"/> on this instance. Safe to call when it was not taken.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
