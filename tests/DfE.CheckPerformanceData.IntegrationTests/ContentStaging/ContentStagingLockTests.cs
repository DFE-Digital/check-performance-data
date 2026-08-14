using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.ContentStaging;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// The import lock is a cross-pod mutex: two administrators confirming an import at the same
// moment on two pods must not both proceed. The controller tests substitute IContentStagingLock,
// so they pin how the controller REACTS to the lock and say nothing about whether the lock
// actually excludes anybody. That question can only be answered against a real Postgres, and it
// is worth answering explicitly — a guard that silently fails open is worse than no guard,
// because everything downstream is written as though it holds.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingLockTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task TryAcquire_WhileAnotherHolderHasIt_IsRefused()
    {
        await using var firstCtx = _fixture.CreateContext();
        await using var secondCtx = _fixture.CreateContext();

        var first = new PostgresContentStagingLock(firstCtx);
        var second = new PostgresContentStagingLock(secondCtx);

        Assert.True(await first.TryAcquireAsync(), "the first caller should take the lock");

        try
        {
            Assert.False(
                await second.TryAcquireAsync(),
                "a second caller must be refused while the first still holds the lock");
        }
        finally
        {
            await first.ReleaseAsync();
        }
    }

    [Fact]
    public async Task TryAcquire_AfterTheHolderReleases_Succeeds()
    {
        await using var firstCtx = _fixture.CreateContext();
        await using var secondCtx = _fixture.CreateContext();

        var first = new PostgresContentStagingLock(firstCtx);
        var second = new PostgresContentStagingLock(secondCtx);

        Assert.True(await first.TryAcquireAsync());
        await first.ReleaseAsync();

        Assert.True(await second.TryAcquireAsync(), "the lock should be free once released");
        await second.ReleaseAsync();
    }

    // Re-entrancy on the same holder is fine (Postgres advisory locks are re-entrant per
    // session), but every acquire needs its matching release or the lock leaks for the
    // connection's lifetime.
    [Fact]
    public async Task Release_WithoutHavingAcquired_DoesNotThrow()
    {
        await using var ctx = _fixture.CreateContext();
        var sut = new PostgresContentStagingLock(ctx);

        await sut.ReleaseAsync();
    }
}
