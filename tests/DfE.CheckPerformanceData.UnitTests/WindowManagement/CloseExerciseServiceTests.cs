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

// The per-exercise close sweep: enqueue this exercise's SubmittedUnCommitted requests to Zendesk,
// commit those rows, and cancel the exercise's leftover drafts.
//
// Replaces AdminRequestsService.ProcessCloseWindowEvent, which swept every OPEN window at once.
// Two things changed with the move and are pinned here: the sweep is scoped to one window AND one
// exercise, and it ignores the exercise's dates entirely.
public sealed class CloseExerciseServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private static readonly Guid RowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
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

    public CloseExerciseServiceTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Flow);
        _flowService.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>()).Returns("Remove");

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
        Id = Guid.NewGuid(), Firstname = "Alice", Surname = "Smith", Sex = "F",
        DateOfBirth = "01/09/2010", Age = 15, Cypmd_Id = "", Identifier = "A86040700001B"
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
    public async Task An_amendment_is_enqueued_and_committed()
    {
        Seed((Row(RowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove));

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.ZendeskQueue, Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SetStatusAsync(
            RowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_sweep_asks_only_for_the_named_window_and_exercise()
    {
        // The whole point of the move: a press on one exercise cannot reach another window's rows,
        // or another exercise's rows in this window.
        Seed();

        await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        await _repository.Received(1).GetRequestsForExerciseAsync(
            WindowId, Exercise, Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(
            WindowId, Exercise, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_for_the_exercise_are_cancelled_and_counted()
    {
        Seed();
        _repository.MarkDraftsNotSubmittedForExerciseAsync(
            WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(3);

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(3, result.DraftsCancelled);
    }

    [Fact]
    public async Task A_row_with_no_request_state_blob_is_skipped_without_stopping_the_sweep()
    {
        var orphanId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _repository.GetRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns([Row(orphanId, "CYPMD_KS4June_MISSING"), Row(RowId, "CYPMD_KS4June_AAAAAA1")]);
        _stateBlob.GetAsync(WindowId, "CYPMD_KS4June_MISSING").Returns((RequestState?)null);
        _stateBlob.GetAsync(WindowId, "CYPMD_KS4June_AAAAAA1").Returns(Journey(WhatToChange.Remove));

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        await _repository.DidNotReceive().SetStatusAsync(
            orphanId, Arg.Any<RequestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Closing_does_not_depend_on_the_exercise_dates()
    {
        // Deliberate: the close button works regardless of the exercise's end date, so the service
        // takes no TimeProvider and asks the repository for no date. If a date filter ever creeps
        // back in, this window (open until 2026-06-30) would stop being swept.
        Seed((Row(RowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove));

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
    }

    [Fact]
    public async Task Preview_counts_the_rows_the_sweep_would_take_without_writing_anything()
    {
        Seed(
            (Row(RowId, "CYPMD_KS4June_AAAAAA1"), WhatToChange.Remove),
            (Row(Guid.NewGuid(), "CYPMD_KS4June_AAAAAA2"), WhatToChange.Include));
        _repository.CountDraftsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(2);

        var preview = await _sut.PreviewAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(2, preview.RequestsToClose);
        Assert.Equal(2, preview.DraftsToCancel);
        await _queueService.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default(object)!, default);
        await _repository.DidNotReceiveWithAnyArgs().SetStatusAsync(default, default, default);
        await _repository.DidNotReceiveWithAnyArgs().MarkDraftsNotSubmittedForExerciseAsync(default, default, default);
    }
}
