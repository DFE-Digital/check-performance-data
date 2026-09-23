using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The pull returns EVERYTHING for the window and output types, decision unfiltered (AB#294553
// "Retrieval"), and refuses before touching Zendesk when a run already holds the pair.
public sealed class EgressRunServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly EgressActor Actor = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Ops One", "ops@example.com");

    private readonly IEgressRunRepository _repo = Substitute.For<IEgressRunRepository>();
    private readonly IEgressTicketSource _tickets = Substitute.For<IEgressTicketSource>();
    private readonly IRequestStateBlobClient _blobs = Substitute.For<IRequestStateBlobClient>();
    private readonly IWindowService _windows = Substitute.For<IWindowService>();

    private EgressRunService Sut() => new(_repo, _tickets, _blobs, _windows, Substitute.For<ILogger<EgressRunService>>());

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "KS4 June 2026", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
    };

    private static EgressCandidateRequest Candidate(string reference, string? crm) =>
        new(Guid.NewGuid(), reference, crm, 142313, "860/4070", new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), RequestStatus.SubmittedCommitted);

    private static RequestState Journey(string reason) => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(), Firstname = "Alice", Surname = "Smith", Sex = "F", DateOfBirth = "07/09/2010", Age = 15,
            Cypmd_Id = "500001", Identifier = "A860407000011", MatchRef = 555, Laestab = "8604070"
        },
        QuestionAnswers = { ["reason"] = new QuestionAnswer { TextValue = reason } }
    };

    [Fact]
    public async Task Refuses_before_calling_zendesk_when_a_pair_is_held()
    {
        var blocker = new EgressBlocker(Guid.NewGuid(), EgressRunStatus.Pulled, "Ops Two", DateTime.UtcNow, null, null);
        _repo.FindBlockerAsync(WindowId, EgressOutputType.RemoveLearners, Arg.Any<CancellationToken>()).Returns(blocker);
        _repo.FindBlockerAsync(WindowId, EgressOutputType.NewLearners, Arg.Any<CancellationToken>()).Returns((EgressBlocker?)null);

        var result = await Sut().StartAsync(WindowId, [EgressOutputType.NewLearners, EgressOutputType.RemoveLearners], Actor, CancellationToken.None);

        var refused = Assert.IsType<EgressStartResult.Refused>(result);
        var (type, who) = Assert.Single(refused.Blockers);
        Assert.Equal(EgressOutputType.RemoveLearners, type);
        Assert.Equal("Ops Two", who.StartedByName);
        await _tickets.DidNotReceiveWithAnyArgs().GetDecisionStatusesAsync(default!, default);
        await _repo.DidNotReceiveWithAnyArgs().CreateRunAsync(default!, default);
    }

    [Fact]
    public async Task Pulls_every_candidate_with_its_decision_and_journey_values()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _repo.GetCandidateRequestsAsync(WindowId, WhatToChange.Remove, Arg.Any<CancellationToken>())
            .Returns([Candidate("REF-1", "1001"), Candidate("REF-2", "1002"), Candidate("REF-3", null), Candidate("REF-4", "1004")]);
        _tickets.GetDecisionStatusesAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1001, 1002, 1004 })), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, string> { [1001] = "auto_approved", [1002] = "rejected" });
        _blobs.GetAsync(WindowId, Arg.Any<string>()).Returns(ci => Journey("pupil-died"));
        _blobs.GetAsync(WindowId, "REF-4").Returns((RequestState?)null);
        EgressRunCreate? created = null;
        _repo.CreateRunAsync(Arg.Do<EgressRunCreate>(c => created = c), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await Sut().StartAsync(WindowId, [EgressOutputType.RemoveLearners], Actor, CancellationToken.None);

        Assert.IsType<EgressStartResult.Started>(result);
        var output = Assert.Single(created!.Outputs);
        Assert.Equal(EgressOutputType.RemoveLearners, output.OutputType);
        Assert.Equal(4, output.Records.Count);
        var byRef = output.Records.ToDictionary(r => r.ReferenceNumber);
        Assert.Equal("auto_approved", byRef["REF-1"].Decision);
        Assert.Equal("rejected", byRef["REF-2"].Decision);
        Assert.Equal(EgressDecisions.NoTicket, byRef["REF-3"].Decision);
        Assert.Equal(EgressDecisions.NotFound, byRef["REF-4"].Decision);
        Assert.Equal(1001, byRef["REF-1"].TicketId);
        Assert.Equal("Smith", byRef["REF-1"].PupilSurname);
        Assert.Equal("8604070", byRef["REF-1"].PupilLaestab);
        Assert.Equal(555, byRef["REF-1"].PupilMatchRef);
        Assert.Equal("pupil-died", byRef["REF-1"].Answer("reason"));
        Assert.Equal(CheckingWindowType.KS4June, byRef["REF-1"].WindowType);
        Assert.True(byRef["REF-1"].JourneyFound);
        Assert.False(byRef["REF-4"].JourneyFound);
        Assert.Equal("Ops One", created.StartedByName);
    }

    // S10: only a null journey (not found) was tested — a genuinely failed read (blob storage
    // unreachable) must be caught per-record too, not fail the whole pull.
    [Fact]
    public async Task A_journey_blob_read_that_throws_is_treated_the_same_as_not_found()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _repo.GetCandidateRequestsAsync(WindowId, WhatToChange.Remove, Arg.Any<CancellationToken>())
            .Returns([Candidate("REF-1", "1001")]);
        _tickets.GetDecisionStatusesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, string> { [1001] = "auto_approved" });
        _blobs.GetAsync(WindowId, "REF-1").Returns(Task.FromException<RequestState?>(new InvalidOperationException("blob storage unreachable")));
        EgressRunCreate? created = null;
        _repo.CreateRunAsync(Arg.Do<EgressRunCreate>(c => created = c), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await Sut().StartAsync(WindowId, [EgressOutputType.RemoveLearners], Actor, CancellationToken.None);

        Assert.IsType<EgressStartResult.Started>(result);
        var record = Assert.Single(created!.Outputs).Records.Single();
        Assert.False(record.JourneyFound);
        Assert.Equal("auto_approved", record.Decision);
    }

    [Fact]
    public async Task A_conflict_on_insert_is_reported_as_a_refusal_with_the_current_blocker()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _repo.GetCandidateRequestsAsync(WindowId, WhatToChange.Add, Arg.Any<CancellationToken>()).Returns([]);
        _tickets.GetDecisionStatusesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<long, string>());
        _repo.CreateRunAsync(Arg.Any<EgressRunCreate>(), Arg.Any<CancellationToken>())
            .Returns<Task<Guid>>(_ => throw new EgressRunConflictException("held"));
        var blocker = new EgressBlocker(Guid.NewGuid(), EgressRunStatus.Transferred, "Ops Two", DateTime.UtcNow, DateTime.UtcNow, "Ops Two");
        _repo.FindBlockerAsync(WindowId, EgressOutputType.NewLearners, Arg.Any<CancellationToken>()).Returns((EgressBlocker?)null, blocker);

        var result = await Sut().StartAsync(WindowId, [EgressOutputType.NewLearners], Actor, CancellationToken.None);

        var refused = Assert.IsType<EgressStartResult.Refused>(result);
        Assert.Equal(EgressRunStatus.Transferred, refused.Blockers[0].Blocker.Status);
    }

    [Fact]
    public async Task A_ticket_source_failure_is_reported_not_thrown()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _repo.GetCandidateRequestsAsync(WindowId, WhatToChange.Remove, Arg.Any<CancellationToken>()).Returns([Candidate("REF-1", "1001")]);
        _tickets.GetDecisionStatusesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyDictionary<long, string>>>(_ => throw new EgressTicketSourceException("field not configured"));

        var result = await Sut().StartAsync(WindowId, [EgressOutputType.RemoveLearners], Actor, CancellationToken.None);

        var failed = Assert.IsType<EgressStartResult.PullFailed>(result);
        Assert.Equal("field not configured", failed.Reason);
        await _repo.DidNotReceiveWithAnyArgs().CreateRunAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_window_is_reported()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);
        Assert.IsType<EgressStartResult.WindowNotFound>(await Sut().StartAsync(WindowId, [EgressOutputType.NewLearners], Actor, CancellationToken.None));
    }

    [Fact]
    public async Task No_output_types_is_refused_as_an_argument_error()
        => await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartAsync(WindowId, [], Actor, CancellationToken.None));

    // The history is the repository's query verbatim: no re-filtering, no re-paging in the service.
    [Fact]
    public async Task History_passes_the_filter_page_and_size_straight_through_to_the_repository()
    {
        var filter = new EgressRunHistoryFilter(WindowId, EgressRunOutcome.Failed);
        var expected = new EgressRunHistoryPage([], 0, 1, 20);
        _repo.ListHistoryAsync(filter, 3, 20, Arg.Any<CancellationToken>()).Returns(expected);

        var page = await Sut().ListHistoryAsync(filter, 3, 20, CancellationToken.None);

        Assert.Same(expected, page);
        await _repo.Received(1).ListHistoryAsync(filter, 3, 20, Arg.Any<CancellationToken>());
    }
}
