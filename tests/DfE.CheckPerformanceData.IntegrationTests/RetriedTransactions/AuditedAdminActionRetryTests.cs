using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: an admin action writes its audit row and its mutation in one transaction. The audit row
// is tracked until the save. A retry must not save the failed attempt's audit row as well.
[Collection(nameof(PostgresCollection))]
public sealed class AuditedAdminActionRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static ICurrentUserService Tester()
    {
        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns("tester");
        return user;
    }

    [Fact]
    public async Task Queue_admin_action_that_is_retried_after_a_transient_fault_writes_one_audit_row()
    {
        await using (var reset = _fixture.CreateContext())
        {
            await reset.Database.ExecuteSqlRawAsync(@"TRUNCATE ""AuditEntries"" RESTART IDENTITY CASCADE;");
        }

        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"AuditEntries\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);
        var adminService = Substitute.For<IQueueAdminService>();
        var controller = new QueueAdminController(adminService, dbContext: ctx, currentUserService: Tester());
        var id = Guid.NewGuid();

        await controller.Redrive([id]);

        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a => a.EntityType == "DlqMessage"));
        // The audit is written before the mutation, so the failed attempt never reached it.
        await adminService.Received(1).RedriveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_session_purge_that_is_retried_after_a_transient_fault_writes_one_audit_row()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        await using (var reset = _fixture.CreateContext())
        {
            await reset.Database.ExecuteSqlRawAsync(@"TRUNCATE ""AuditEntries"" RESTART IDENTITY CASCADE;");
            reset.SearchEvents.Add(new SearchEvent
            {
                OccurredAtUtc = DateTime.UtcNow,
                SessionId = "s-1",
                QueryRaw = "q",
                QueryNormalised = "q",
                ResultsPages = 1,
                ResultsBlocks = 0,
                LatencyMs = 10,
            });
            await reset.SaveChangesAsync();
        }

        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"AuditEntries\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);
        var messages = Substitute.For<ISearchMessageService>();
        messages.PurgeSessionAsync("s-1", Arg.Any<CancellationToken>()).Returns(new SessionPurgeCounts(0));
        var controller = new SearchAnalyticsController(
            Substitute.For<ISearchAnalyticsQueryService>(), Substitute.For<ISettingService>(),
            messages, ctx, Tester());

        await controller.Delete("s-1");

        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == "SearchSession" && a.EntityId == "s-1"));
        Assert.False(await verify.SearchEvents.AnyAsync(e => e.SessionId == "s-1"));
    }
}
