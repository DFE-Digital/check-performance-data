using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Locking;

namespace DfE.CheckPerformanceData.IntegrationTests.Impersonation;

[Collection(nameof(PostgresCollection))]
public sealed class ImpersonationLockTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Separate_instances_serialize_the_same_session_but_allow_another_session()
    {
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = new PostgresImpersonationSessionLock(firstDb);
        var second = new PostgresImpersonationSessionLock(secondDb);
        var binding = Guid.NewGuid().ToString();
        var lease = await first.AcquireAsync(binding, default);
        try
        {
            await using var unrelated = await second.AcquireAsync(Guid.NewGuid().ToString(), default);
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await using var unexpected = await second.AcquireAsync(binding, cancelled.Token);
            });
        }
        finally { await lease.DisposeAsync(); }
        await using var released = await second.AcquireAsync(binding, default);
    }
}
