using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: ReplaceRoleAsync adds rows to the change tracker. A retry must not insert them twice.
[Collection(nameof(PostgresCollection))]
public sealed class AdminSectionAccessRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Replace_that_is_retried_after_a_transient_fault_writes_each_section_once()
    {
        await using (var seed = _fixture.CreateContext())
        {
            await seed.Database.ExecuteSqlRawAsync(
                @"TRUNCATE ""AdminSectionAccesses"", ""AuditEntries"" RESTART IDENTITY CASCADE;");
            await new AdminSectionAccessRepository(seed).ReplaceRoleAsync("editor", ["old"], "seed");
        }

        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"AdminSectionAccesses\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new AdminSectionAccessRepository(ctx).ReplaceRoleAsync("editor", ["a", "b"], "tester");

        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        var sections = await verify.AdminSectionAccesses
            .Where(a => a.RoleName == "editor").Select(a => a.SectionKey).OrderBy(s => s).ToListAsync();
        Assert.Equal(["a", "b"], sections);
    }
}
