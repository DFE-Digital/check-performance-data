using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.UncommittedRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.UncommittedRequests;

// AB#297310: the window-close Zendesk replay must skip Add-a-pupil requests.
//
// Ticket B2 ("No rules engine outcomes") means an Add request never goes to the rules engine or
// Zendesk — its downstream is the LDS egress, a separate story. This replay path also builds a
// pupil-amendment ticket, which an Add doesn't fit, and flipping the row to SubmittedCommitted
// would hide it from that future egress.
public sealed class AdminRequestsServiceAddGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private static readonly Guid AmendmentRowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AddRowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IUncommittedRequestsRepository _repository = Substitute.For<IUncommittedRequestsRepository>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly AdminRequestsService _sut;

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "page-1",
        Pages = [new JourneyPage { Id = "page-1" }]
    };

    public AdminRequestsServiceAddGuardTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Flow);
        _flowService.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>()).Returns("Remove");

        _sut = new AdminRequestsService(
            _repository, _stateBlob, _flowService, _queueService, new FakeTimeProvider(Now));
    }

    private static ReplayRequestRow Row(Guid id, string reference) => new()
    {
        ChangeRequestId = id,
        WindowId = WindowId,
        ReferenceNumber = reference,
        OrganisationUrn = 142313,
        SubmittedById = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        SubmittedByName = "Ada Editor"
    };

    private static PupilDto Pupil() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Alice", Surname = "Newpupil", Sex = "F",
        DateOfBirth = "01/09/2010", Age = 0, Cypmd_Id = "", Identifier = "A123456789012"
    };

    private static CheckingWindowDto Window => new()
    {
        Title = "KS4 June 2026", KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
    };

    private static RequestState Journey(WhatToChange change) => new()
    {
        SelectedWhatToChange = change,
        CheckingWindow = Window,
        SelectedPupil = Pupil(),
        QuestionAnswers = [],
        QuestionHistory = ["page-1"]
    };

    private void Seed(params (ReplayRequestRow Row, WhatToChange Change)[] rows)
    {
        _repository.GetRequestsForOpenWindowsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => r.Row).ToList());
        foreach (var (row, change) in rows)
            _stateBlob.GetAsync(WindowId, row.ReferenceNumber).Returns(Journey(change));
    }

    [Fact]
    public async Task An_add_request_is_not_replayed()
    {
        Seed((Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(0, count);
        await _queueService.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default(object)!, default);
    }

    [Fact]
    public async Task An_add_request_row_keeps_its_uncommitted_status()
    {
        // The important half: committing it would hide the request from the future LDS egress.
        Seed((Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        await _repository.DidNotReceive().SetStatusAsync(
            AddRowId, Arg.Any<RequestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_mixed_batch_replays_only_the_amendment()
    {
        Seed(
            (Row(AmendmentRowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove),
            (Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, count);
        await _repository.Received(1).SetStatusAsync(
            AmendmentRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SetStatusAsync(
            AddRowId, Arg.Any<RequestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_add_request_does_not_stop_a_later_amendment_being_replayed()
    {
        // Ordering matters: a `continue` skips one row, but a `return`/`break` would silently drop
        // every amendment queued behind it.
        Seed(
            (Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add),
            (Row(AmendmentRowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, count);
        await _repository.Received(1).SetStatusAsync(
            AmendmentRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_are_still_marked_not_submitted()
    {
        // The guard must not short-circuit the rest of the close-window work.
        Seed((Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        await _repository.Received(1).MarkDraftsNotSubmittedForOpenWindowsAsync(
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
