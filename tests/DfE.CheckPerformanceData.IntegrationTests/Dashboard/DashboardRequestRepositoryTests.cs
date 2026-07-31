using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;

namespace DfE.CheckPerformanceData.IntegrationTests.Dashboard;

[Collection(nameof(PostgresCollection))]
public sealed class DashboardRequestRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetRequestAggregatesAsync_CountsOnlySubmittedStatuses_AndGroupsOutcomes()
    {
        var windowId = await SeedWindowAsync();
        var otherWindowId = await SeedWindowAsync();

        // In-window: 2 submitted from URN 111 (one approved, one undecided),
        // 1 submitted from URN 222 (rejected), 1 scrutiny from URN 111,
        // plus one InProgress and one Withdrawn which must NOT count.
        await SeedRequestAsync(windowId, urn: 111, RequestStatus.SubmittedUnCommitted, DecisionStatus.AutoApproved);
        await SeedRequestAsync(windowId, urn: 111, RequestStatus.SubmittedCommitted, outcome: null);
        await SeedRequestAsync(windowId, urn: 222, RequestStatus.SubmittedUnCommitted, DecisionStatus.AutoRejected);
        await SeedRequestAsync(windowId, urn: 111, RequestStatus.SubmittedUnCommitted, DecisionStatus.Scrutiny);
        await SeedRequestAsync(windowId, urn: 333, RequestStatus.InProgress, outcome: null);
        await SeedRequestAsync(windowId, urn: 444, RequestStatus.Withdrawn, outcome: null);
        // Different window: must not count at all.
        await SeedRequestAsync(otherWindowId, urn: 555, RequestStatus.SubmittedUnCommitted, DecisionStatus.AutoApproved);

        await using var context = fixture.CreateContext();
        var result = await new DashboardRequestRepository(context).GetRequestAggregatesAsync(windowId);

        Assert.Equal(4, result.TotalRequests);
        Assert.Equal(1, result.AutoApproved);
        Assert.Equal(1, result.AutoRejected);
        Assert.Equal(1, result.RequiringScrutiny);
        Assert.Equal(new[] { 111L, 222L }, result.SubmittingUrns.OrderBy(u => u).ToArray());
    }

    private async Task<Guid> SeedWindowAsync()
    {
        await using var context = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified),
        };
        context.CheckingWindows.Add(window);
        await context.SaveChangesAsync();
        return window.Id;
    }

    private async Task SeedRequestAsync(
        Guid windowId, long urn, RequestStatus status, DecisionStatus? outcome)
    {
        await using var context = fixture.CreateContext();
        context.ChangeRequests.Add(new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = windowId,
            OrganisationUrn = urn,
            Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            WorkerStatus = WorkerStatus.RulesProcessed,
            Status = status,
            ReferenceNumber = Guid.NewGuid().ToString("N"),
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Not on roll",
            Outcome = outcome,
        });
        await context.SaveChangesAsync();
    }
}
