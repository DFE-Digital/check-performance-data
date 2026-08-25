using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// Locks the semantic change to the in-memory job store: Cancel is no longer a "signal the
// token if running" primitive. Rollback of already-completed jobs is a legitimate action,
// so RequestCancel must accept any non-terminal state (Running or the new Cancelling
// intermediate) AND must accept a Completed state as well — the caller (controller) uses
// the return value only to decide whether the job id is known, not whether the seeder was
// actually in flight. When the job is Running, the tracked CancellationTokenSource is
// signalled and the state transitions to Cancelling so the poller can render a
// "cancelling…" UI while the seeder unwinds. When the job is already Completed, the store
// still returns true so the controller can proceed with rollback.
public sealed class SampleSearchDataSeedJobStoreTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static InMemorySampleSearchDataSeedJobStore CreateSut() =>
        new(new FakeTimeProvider(DateTimeOffset.Parse("2026-07-29T12:00:00Z")));

    [Fact]
    public void RequestCancel_OnRunningJob_SignalsTokenAndTransitionsToCancelling()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var job = sut.Register("the last 24 hours", 500, 40, cts);

        var accepted = sut.RequestCancel(job.JobId);

        Assert.True(accepted);
        Assert.True(cts.IsCancellationRequested,
            "Cancel on a Running job must fire the tracked CancellationTokenSource.");

        var snapshot = sut.Get(job.JobId);
        Assert.NotNull(snapshot);
        Assert.Equal(SampleSearchDataSeedJobState.Cancelling, snapshot!.State);
    }

    [Fact]
    public void RequestCancel_OnCompletedJob_StillReturnsTrue_ForRollbackHandoff()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var job = sut.Register("the last 24 hours", 500, 40, cts);

        sut.MarkCompleted(job.JobId, auditEntryId: 42);

        // Completed → Cancel is a valid transition here because the controller uses the
        // Cancel/DELETE endpoint to rollback the rows the job wrote, whether or not the
        // seeder was still in flight. The store must NOT return 404-style false here.
        var accepted = sut.RequestCancel(job.JobId);

        Assert.True(accepted);
        var snapshot = sut.Get(job.JobId);
        Assert.NotNull(snapshot);
        // State stays Completed — the seeder already finished; the controller is only
        // asking the store whether the id exists so it can go ahead with rollback.
        Assert.Equal(SampleSearchDataSeedJobState.Completed, snapshot!.State);
    }

    [Fact]
    public void RequestCancel_OnFailedJob_ReturnsTrue_ForRollbackHandoff()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var job = sut.Register("the last 24 hours", 500, 40, cts);

        sut.MarkFailed(job.JobId, "boom");

        Assert.True(sut.RequestCancel(job.JobId));
    }

    [Fact]
    public void RequestCancel_OnUnknownJob_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.RequestCancel(Guid.NewGuid()));
    }

    [Fact]
    public void MarkCancelling_TransitionsRunningJobToCancellingState()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var job = sut.Register("the last 24 hours", 500, 40, cts);

        sut.MarkCancelling(job.JobId);

        var snapshot = sut.Get(job.JobId);
        Assert.NotNull(snapshot);
        Assert.Equal(SampleSearchDataSeedJobState.Cancelling, snapshot!.State);
    }
}
