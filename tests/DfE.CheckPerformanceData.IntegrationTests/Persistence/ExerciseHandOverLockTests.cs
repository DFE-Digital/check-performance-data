using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Locking;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AB#302158: the automatic hand-over runs in every web pod, and only one of them may sweep at a
// time. The job's unit tests substitute the lock, so they pin how the job REACTS to it and say
// nothing about whether it excludes anybody. Only a real Postgres can answer that.
[Collection(nameof(PostgresCollection))]
public sealed class ExerciseHandOverLockTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_second_pod_is_refused_while_the_first_holds_the_lock()
    {
        await using var firstCtx = fixture.CreateContext();
        await using var secondCtx = fixture.CreateContext();
        var first = new PostgresExerciseHandOverLock(firstCtx);
        var second = new PostgresExerciseHandOverLock(secondCtx);

        Assert.True(await first.TryAcquireAsync(), "the first pod should take the lock");
        try
        {
            Assert.False(await second.TryAcquireAsync(), "a second pod must be refused while the first holds the lock");
        }
        finally
        {
            await first.ReleaseAsync();
        }
    }

    [Fact]
    public async Task The_lock_is_free_again_once_released()
    {
        await using var firstCtx = fixture.CreateContext();
        await using var secondCtx = fixture.CreateContext();
        var first = new PostgresExerciseHandOverLock(firstCtx);
        var second = new PostgresExerciseHandOverLock(secondCtx);

        Assert.True(await first.TryAcquireAsync());
        await first.ReleaseAsync();

        Assert.True(await second.TryAcquireAsync(), "the next tick, on either pod, must be able to take it");
        await second.ReleaseAsync();
    }

    [Fact]
    public async Task It_does_not_exclude_a_content_staging_import()
    {
        // Two locks on two keys. An import that happens to run during a hand-over tick must not
        // be told "another import is in progress", and the reverse.
        await using var handOverCtx = fixture.CreateContext();
        await using var importCtx = fixture.CreateContext();
        var handOver = new PostgresExerciseHandOverLock(handOverCtx);
        var import = new PostgresContentStagingLock(importCtx);

        Assert.True(await handOver.TryAcquireAsync());
        try
        {
            Assert.True(await import.TryAcquireAsync(), "the import lock is a different key");
            await import.ReleaseAsync();
        }
        finally
        {
            await handOver.ReleaseAsync();
        }
    }
}
