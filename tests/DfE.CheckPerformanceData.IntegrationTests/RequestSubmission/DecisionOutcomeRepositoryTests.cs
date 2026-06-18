using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

[Collection(nameof(PostgresCollection))]
public sealed class DecisionOutcomeRepositoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task RecordOutcome_WritesDecisionToRow_AndReturnsTrue()
    {
        await TruncateAsync();
        var changeRequestId = await SeedChangeRequestAsync();
        var decision = new Decision(DecisionStatus.AutoApproved, "PupilDied", "rule-7", []);

        var updated = await new DecisionOutcomeRepository(_fixture.CreateContext())
            .RecordOutcomeAsync(changeRequestId, decision, CancellationToken.None);

        Assert.True(updated);
        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.Id == changeRequestId);
        Assert.Equal(DecisionStatus.AutoApproved, row.Outcome);
        Assert.Equal("PupilDied", row.OutcomeKey);
        Assert.Equal("rule-7", row.MatchedRuleId);
    }

    [Fact]
    public async Task RecordOutcome_IsIdempotent_RetryRewritesSameValues()
    {
        await TruncateAsync();
        var changeRequestId = await SeedChangeRequestAsync();
        var decision = new Decision(DecisionStatus.Scrutiny, "_unknown", "_engine_error", []);
        var repo = () => new DecisionOutcomeRepository(_fixture.CreateContext());

        await repo().RecordOutcomeAsync(changeRequestId, decision, CancellationToken.None);
        var secondAttempt = await repo().RecordOutcomeAsync(changeRequestId, decision, CancellationToken.None);

        Assert.True(secondAttempt);
        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.Id == changeRequestId);
        Assert.Equal(DecisionStatus.Scrutiny, row.Outcome);
    }

    [Fact]
    public async Task RecordOutcome_WhenNoRowMatches_ReturnsFalse()
    {
        await TruncateAsync();
        var decision = new Decision(DecisionStatus.AutoRejected, "k", "r", []);

        var updated = await new DecisionOutcomeRepository(_fixture.CreateContext())
            .RecordOutcomeAsync(Guid.NewGuid(), decision, CancellationToken.None);

        Assert.False(updated);
    }

    [Fact]
    public async Task UnprocessedRow_HasNullOutcomeColumns()
    {
        await TruncateAsync();
        var changeRequestId = await SeedChangeRequestAsync();

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.Id == changeRequestId);
        Assert.Null(row.Outcome);
        Assert.Null(row.OutcomeKey);
        Assert.Null(row.MatchedRuleId);
    }

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedChangeRequestAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);

        var changeRequest = new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = window.Id,
            OrganisationUrn = 100000,
            Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.SubmittedUnCommitted,
            ReferenceNumber = $"REF-{Guid.NewGuid():N}",
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove"
        };
        ctx.ChangeRequests.Add(changeRequest);
        await ctx.SaveChangesAsync();
        return changeRequest.Id;
    }
}
