using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: the rules configuration repository wrapper must start each run of a retried delegate
// with a clean change tracker.
[Collection(nameof(PostgresCollection))]
public sealed class RulesConfigVersionRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Version_and_audit_that_are_retried_after_a_transient_fault_are_written_once()
    {
        await using (var reset = _fixture.CreateContext())
        {
            await reset.Database.ExecuteSqlRawAsync(
                @"TRUNCATE ""RulesConfigVersions"", ""AuditEntries"" RESTART IDENTITY CASCADE;");
        }

        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"RulesConfigVersions\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);
        var repo = new RulesConfigVersionRepository(ctx);

        // The same three calls as RulesConfigService.PersistAsync.
        var next = 0;
        await repo.ExecuteInTransactionAsync(async () =>
        {
            next = await repo.GetMaxVersionNumberAsync(RulesConfigType.Rules) + 1;
            await repo.AddVersionAsync(RulesConfigType.Rules, next, "{\"v\":1}", "tester", DateTime.UtcNow);
            await repo.AddAuditAsync("RulesConfig", RulesConfigType.Rules.ToString(), "Save", "tester", DateTime.UtcNow);
        });

        Assert.Equal(2, interceptor.Matches);
        Assert.Equal(1, next);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.RulesConfigVersions.CountAsync());
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a => a.EntityType == "RulesConfig" && a.Action == "Save"));
    }
}
