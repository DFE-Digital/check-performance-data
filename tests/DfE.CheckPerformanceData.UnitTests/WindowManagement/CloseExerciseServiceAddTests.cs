using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
// Two unrelated classes are named CheckingWindowDto; RequestState.CheckingWindow is the LandingPage one.
using CheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// An Add-a-pupil request IS swept by the close, like any other pupil-data amendment.
//
// This INVERTS AdminRequestsServiceAddGuardTests, which pinned the opposite. That guard was parked
// under AB#297310 on the reasoning that an Add's downstream is the LDS egress rather than Zendesk,
// so committing the row would hide it from that egress. The egress is not designed yet, so there is
// no contract to protect and no way to know what "closed" should mean for an Add. The decision is
// to sweep it now; AB#297310 inherits it and must say whether these rows need a backfill once the
// egress is specified.
//
// ResultsEnquiry is now the ONLY exclusion — see CloseExerciseServiceEnquiryGuardTests.
public sealed class CloseExerciseServiceAddTests
{
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private static readonly Guid AmendmentRowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AddRowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    private readonly IAdminRequestsRepository _repository = Substitute.For<IAdminRequestsRepository>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly CloseExerciseService _sut;

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "page-1",
        Pages = [new JourneyPage { Id = "page-1" }]
    };

    public CloseExerciseServiceAddTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Flow);
        _flowService.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>()).Returns("Add");

        _sut = new CloseExerciseService(_repository, _stateBlob, _flowService, _queueService);
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
        _repository.GetRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => r.Row).ToList());
        foreach (var (row, change) in rows)
            _stateBlob.GetAsync(WindowId, row.ReferenceNumber).Returns(Journey(change));
    }

    [Fact]
    public async Task An_add_request_is_enqueued()
    {
        Seed((Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.ZendeskQueue, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_add_request_row_is_committed()
    {
        Seed((Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        await _repository.Received(1).SetStatusAsync(
            AddRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_mixed_batch_sweeps_both_the_add_and_the_amendment()
    {
        Seed(
            (Row(AmendmentRowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove),
            (Row(AddRowId, "CYPMD_KS4June_BBBBBB2"), WhatToChange.Add));

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(2, result.Enqueued);
        await _repository.Received(1).SetStatusAsync(
            AmendmentRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
        await _repository.Received(1).SetStatusAsync(
            AddRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
    }
}
