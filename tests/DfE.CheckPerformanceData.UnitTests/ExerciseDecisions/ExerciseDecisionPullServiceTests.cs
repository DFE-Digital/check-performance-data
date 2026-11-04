using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ExerciseDecisions;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ExerciseDecisions;

// The decision pull's bucketing and its decision rules: final reads are stored (latest wins),
// non-final reads are counted but never clear a stored decision, and a source failure refuses
// the whole pull with the reason — nothing written. Ids and counts only, no personal data.
public sealed class ExerciseDecisionPullServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string AdminUserId = "admin-1";

    private readonly IExerciseDecisionPullRepository _repo = Substitute.For<IExerciseDecisionPullRepository>();
    private readonly IEgressTicketSource _tickets = Substitute.For<IEgressTicketSource>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();
    private readonly ILogger<ExerciseDecisionPullService> _logger = Substitute.For<ILogger<ExerciseDecisionPullService>>();

    public ExerciseDecisionPullServiceTests()
    {
        _user.UserId.Returns(AdminUserId);
    }

    private ExerciseDecisionPullService Sut() => new(_repo, _tickets, _user, _logger);

    private static ExerciseDecisionPullRow Row(string crm, ZendeskDecision? stored = null, string? storedReason = null) =>
        new(Guid.NewGuid(), "REF-" + Guid.NewGuid().ToString("N")[..6], crm, stored, storedReason);

    private async Task<(ExerciseDecisionPullResult Result, IReadOnlyList<SavedDecision> Saved, DecisionsPulledAudit? Audit)> PullAsync(
        IReadOnlyList<ExerciseDecisionPullRow> rows,
        IReadOnlyDictionary<long, EgressTicketDecision> tickets)
    {
        _repo.GetSubmittedRowsAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>()).Returns(rows);
        _tickets.GetTicketDecisionsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(tickets);
        IReadOnlyList<SavedDecision> saved = [];
        DecisionsPulledAudit? audit = null;
        _repo.SaveDecisionsAsync(Arg.Do<IReadOnlyList<SavedDecision>>(s => saved = s), Arg.Do<DecisionsPulledAudit>(a => audit = a), Arg.Any<CancellationToken>())
            .Returns(tickets.Count);

        return (await Sut().PullAsync(WindowId, ExerciseId, CancellationToken.None), saved, audit);
    }

    [Fact]
    public async Task Pull_buckets_all_five_outcomes_and_saves_only_the_final_decisions()
    {
        var approved = Row("1001");
        var rejected = Row("1002");
        var scrutiny = Row("1003");
        var noTicket = Row(null);
        var missing = Row("1005");
        var badId = Row("not-a-number");

        var (result, saved, audit) = await PullAsync(
            [approved, rejected, scrutiny, noTicket, missing, badId],
            new Dictionary<long, EgressTicketDecision>
            {
                [1001] = new("approved", "Inclusion criteria met"),
                [1002] = new("auto_rejected", "Inclusion criteria not met"),
                [1003] = new("scrutiny", null)
            });

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(1, result.NoTicket);
        Assert.Equal(2, result.TicketNotFound);
        Assert.Equal(6, result.SubmittedTotal);
        Assert.Null(result.FailureReason);

        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, s => s.ChangeRequestId == approved.ChangeRequestId
            && s.Decision == ZendeskDecision.Accepted && s.Reason == "Inclusion criteria met");
        Assert.Contains(saved, s => s.ChangeRequestId == rejected.ChangeRequestId
            && s.Decision == ZendeskDecision.Rejected && s.Reason == "Inclusion criteria not met");

        Assert.NotNull(audit);
        Assert.Equal(WindowId, audit!.WindowId);
        Assert.Equal(ExerciseId, audit!.ExerciseId);
        Assert.Equal(1, audit!.Accepted);
        Assert.Equal(AdminUserId, audit!.UserId);
    }

    [Fact]
    public async Task Auto_approved_and_auto_rejected_map_the_same_as_their_human_twins()
    {
        var (result, saved, _) = await PullAsync(
            [Row("1"), Row("2")],
            new Dictionary<long, EgressTicketDecision>
            {
                [1] = new("auto_approved", null),
                [2] = new("auto_rejected", null)
            });

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, s => s.Decision == ZendeskDecision.Accepted);
        Assert.Contains(saved, s => s.Decision == ZendeskDecision.Rejected);
    }

    [Fact]
    public async Task A_non_final_read_keeps_a_stored_decision_and_still_writes_the_audit()
    {
        var stored = Row("1001", ZendeskDecision.Accepted, "Inclusion criteria met");

        var (result, saved, audit) = await PullAsync(
            [stored],
            new Dictionary<long, EgressTicketDecision> { [1001] = new("scrutiny", null) });

        Assert.Equal(1, result.Unchanged);
        Assert.Empty(saved);
        Assert.NotNull(audit);
        Assert.Equal(0, audit!.Accepted);
        Assert.Equal(1, audit!.Unchanged);
    }

    [Fact]
    public async Task A_later_final_read_overwrites_the_decision_and_reason_together()
    {
        var stored = Row("1001", ZendeskDecision.Accepted, "An earlier reason");

        var (result, saved, _) = await PullAsync(
            [stored],
            new Dictionary<long, EgressTicketDecision> { [1001] = new("rejected", null) });

        Assert.Equal(1, result.Rejected);
        var overwritten = Assert.Single(saved);
        Assert.Equal(stored.ChangeRequestId, overwritten.ChangeRequestId);
        Assert.Equal(ZendeskDecision.Rejected, overwritten.Decision);
        Assert.Null(overwritten.Reason);
    }

    [Fact]
    public async Task A_source_exception_returns_the_failure_reason_and_writes_nothing()
    {
        _repo.GetSubmittedRowsAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns([Row("1001")]);
        _tickets.GetTicketDecisionsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyDictionary<long, EgressTicketDecision>>>(
                _ => throw new EgressTicketSourceException("Zendesk could not return tickets 1001."));

        var result = await Sut().PullAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal("Zendesk could not return tickets 1001.", result.FailureReason);
        Assert.Equal(0, result.Accepted + result.Rejected + result.Unchanged + result.NoTicket + result.TicketNotFound);
        await _repo.DidNotReceiveWithAnyArgs().SaveDecisionsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Zero_submitted_rows_yield_an_all_zero_result_without_calling_zendesk()
    {
        _repo.GetSubmittedRowsAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>()).Returns([]);
        DecisionsPulledAudit? audit = null;
        _repo.SaveDecisionsAsync(Arg.Any<IReadOnlyList<SavedDecision>>(), Arg.Do<DecisionsPulledAudit>(a => audit = a), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await Sut().PullAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(0, result.SubmittedTotal);
        Assert.Null(result.FailureReason);
        await _tickets.DidNotReceiveWithAnyArgs().GetTicketDecisionsAsync(default!, default);
        Assert.NotNull(audit);
        Assert.Equal(0, audit!.SubmittedTotal);
    }

    [Fact]
    public async Task Preview_counts_submitted_requests_and_the_ones_with_a_ticket()
    {
        _repo.GetSubmittedRowsAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns([Row("1001"), Row("1002"), Row(null), Row("junk")]);

        var preview = await Sut().PreviewAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(4, preview.SubmittedTotal);
        Assert.Equal(2, preview.WithTicket);
        await _tickets.DidNotReceiveWithAnyArgs().GetTicketDecisionsAsync(default!, default);
    }
}
