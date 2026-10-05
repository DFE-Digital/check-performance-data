using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Aliases, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class BulkSubmissionServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const long Urn = 100000L;

    private static readonly Guid PupilA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PupilB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PupilC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IRequestRepository _repo = Substitute.For<IRequestRepository>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IRequestNotificationService _notify = Substitute.For<IRequestNotificationService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly BulkSubmissionService _sut;

    public BulkSubmissionServiceTests()
    {
        _currentUser.OrganisationUrn.Returns(Urn.ToString());
        _sut = new BulkSubmissionService(_repo, _requestService, _notify, _pupilData, _currentUser, _analytics, _checkingExercises);
    }

    private static AmendmentRequestData Row(string reference, Guid pupilId, string first, string surname,
        RequestStatus status = RequestStatus.ReadyToSubmit) => new()
    {
        ReferenceNumber = reference,
        PupilId = pupilId,
        PupilFirstname = first,
        PupilSurname = surname,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove pupil",
        Status = status
    };

    // ---- BuildReviewAsync (Task 9) ----

    [Fact]
    public async Task BuildReview_AllUnique_AllSubmittable()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());

        var result = await _sut.BuildReviewAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(2, result.Submittable.Count);
        Assert.Empty(result.Duplicates);
    }

    [Fact]
    public async Task BuildReview_PupilAlreadySubmitted_IsDuplicate()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[] { Row("R1", PupilA, "Ann", "Alpha") });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(new[] { PupilA });

        var result = await _sut.BuildReviewAsync(WindowId, new[] { "R1" });

        Assert.Empty(result.Submittable);
        Assert.Single(result.Duplicates);
        Assert.Equal("R1", result.Duplicates[0].ReferenceNumber);
    }

    [Fact]
    public async Task BuildReview_SamePupilSelectedTwice_BothDuplicatesNeitherSubmittable()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilA, "Ann", "Alpha"),
            Row("R3", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());

        var result = await _sut.BuildReviewAsync(WindowId, new[] { "R1", "R2", "R3" });

        Assert.Equal(new[] { "R3" }, result.Submittable.Select(x => x.ReferenceNumber));
        Assert.Equal(new[] { "R1", "R2" }, result.Duplicates.Select(x => x.ReferenceNumber).OrderBy(x => x));
    }

    [Fact]
    public async Task BuildReview_IgnoresUnselectedAndNonReadyToSubmitRows()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta", RequestStatus.InProgress),
            Row("R3", PupilC, "Cam", "Gamma")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());

        var result = await _sut.BuildReviewAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(new[] { "R1" }, result.Submittable.Select(x => x.ReferenceNumber));
        Assert.Empty(result.Duplicates);
    }

    // ---- SubmitAsync (Task 10) ----

    private void SetupWindowDeadline()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _pupilData.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 2026",
            EndDate = endDate,
            StartDate = endDate.AddMonths(-3),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June
        });
    }

    private void SetupDraft(string reference)
    {
        var state = new RequestState { ReferenceNumber = reference };
        _requestService.ResumeDraftAsync(WindowId, reference).Returns(state);
    }

    [Fact]
    public async Task Submit_SubmitsEachSubmittable_AndReturnsReferences()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
        SetupWindowDeadline();
        SetupDraft("R1");
        SetupDraft("R2");

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(new[] { "R1", "R2" }, result.Submitted);
        await _requestService.Received(1).SubmitRequestAsync(WindowId, Arg.Is<RequestState>(s => s.ReferenceNumber == "R1"));
        await _requestService.Received(1).SubmitRequestAsync(WindowId, Arg.Is<RequestState>(s => s.ReferenceNumber == "R2"));
        await _notify.Received(1).NotifyBulkSubmissionConfirmedAsync(
            WindowId, Arg.Any<DateTime>(), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "R1", "R2" })), Arg.Any<EmailSubstitutions>());
    }

    [Fact]
    public async Task Submit_SkipsConflictingRequest_AndSubmitsTheRest()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
        SetupWindowDeadline();
        SetupDraft("R1");
        SetupDraft("R2");
        _requestService.SubmitRequestAsync(WindowId, Arg.Is<RequestState>(s => s.ReferenceNumber == "R1"))
            .Returns(Task.FromException(new DuplicateRequestException(ConflictType.SelfSubmitted)));

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(new[] { "R2" }, result.Submitted);
        Assert.Equal(new[] { "R1" }, result.Skipped);
        await _notify.Received(1).NotifyBulkSubmissionConfirmedAsync(
            WindowId, Arg.Any<DateTime>(), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "R2" })), Arg.Any<EmailSubstitutions>());
    }

    [Fact]
    public async Task Submit_MissingDraft_IsSkippedNotSubmitted()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
        SetupWindowDeadline();
        SetupDraft("R1");
        _requestService.ResumeDraftAsync(WindowId, "R2").Returns((RequestState?)null); // draft blob missing

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(new[] { "R1" }, result.Submitted);
        Assert.Equal(new[] { "R2" }, result.Skipped);
        await _requestService.DidNotReceive().SubmitRequestAsync(WindowId, Arg.Is<RequestState>(s => s.ReferenceNumber == "R2"));
    }

    [Fact]
    public async Task Submit_EmptySelection_SubmitsNothingAndSendsNoEmail()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(Array.Empty<AmendmentRequestData>());
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());

        var result = await _sut.SubmitAsync(WindowId, Array.Empty<string>());

        Assert.Empty(result.Submitted);
        await _notify.DidNotReceive().NotifyBulkSubmissionConfirmedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<EmailSubstitutions>());
    }

    [Fact]
    public async Task Submit_EmitsRequestSubmittedEventPerSubmittedReference()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilB, "Ben", "Beta")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
        SetupWindowDeadline();
        SetupDraft("R1");
        SetupDraft("R2");
        _requestService.SubmitRequestAsync(WindowId, Arg.Is<RequestState>(s => s.ReferenceNumber == "R1"))
            .Returns(Task.FromException(new DuplicateRequestException(ConflictType.SelfSubmitted))); // R1 conflicts, only R2 submits

        await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        await _analytics.Received(1).TrackAsync(
            Arg.Is<RequestSubmittedEvent>(e => e.ReferenceNumber == "R2"),
            Arg.Any<CancellationToken>());
        await _analytics.DidNotReceive().TrackAsync(
            Arg.Is<RequestSubmittedEvent>(e => e.ReferenceNumber == "R1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ExcludesDuplicatesFromClassification()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[]
        {
            Row("R1", PupilA, "Ann", "Alpha"),
            Row("R2", PupilA, "Ann", "Alpha")
        });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
        SetupWindowDeadline();

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        Assert.Empty(result.Submitted);
        await _requestService.DidNotReceive().SubmitRequestAsync(Arg.Any<Guid>(), Arg.Any<RequestState>());
        await _notify.DidNotReceive().NotifyBulkSubmissionConfirmedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<EmailSubstitutions>());
    }

    // ---- AB#301022: a draft whose checking exercise has closed ----
    //
    // Bulk submit had no open-exercise check of its own. After a scheduled end that left a
    // ReadyToSubmit draft submittable until an admin ran the hand-over; after an early close it
    // left the same gap for as long as the hand-over took, or for good if it failed.

    private static readonly DateTime FarFuture = new(2027, 8, 2, 17, 0, 0);

    private void SetupRemoveDraft(string reference, List<CheckingExerciseDto>? snapshot = null)
    {
        var state = new RequestState
        {
            ReferenceNumber = reference,
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = WindowWith(snapshot ?? [])
        };
        _requestService.ResumeDraftAsync(WindowId, reference).Returns(state);
    }

    private static CheckingWindowDto WindowWith(List<CheckingExerciseDto> exercises) => new()
    {
        Id = WindowId,
        Title = "KS4 2026",
        EndDate = FarFuture,
        StartDate = FarFuture.AddYears(-1),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        Exercises = exercises
    };

    private static List<CheckingExerciseDto> PupilDataEnding(DateTime end) =>
    [
        new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = FarFuture.AddYears(-1),
            EndDate = end,
            TabOrder = 0
        }
    ];

    private void OneReadyDraft()
    {
        _repo.GetAmendmentRequestsAsync(WindowId, Urn).Returns(new[] { Row("R1", PupilA, "Ann", "Alpha") });
        _repo.GetSubmittedPupilIdsAsync(WindowId, Urn).Returns(Array.Empty<Guid>());
    }

    [Fact]
    public async Task Submit_WhenTheDraftsExerciseHasClosed_SubmitsNothingAndSaysWhichExercise()
    {
        OneReadyDraft();
        SetupWindowDeadline();
        SetupRemoveDraft("R1");
        _checkingExercises.Close();

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1" });

        Assert.Empty(result.Submitted);
        Assert.Equal(new[] { "R1" }, result.Skipped);
        Assert.Equal(CheckingExerciseType.PupilData, result.ClosedExercise);
        await _requestService.DidNotReceive().SubmitRequestAsync(Arg.Any<Guid>(), Arg.Any<RequestState>());
        await _notify.DidNotReceive().NotifyBulkSubmissionConfirmedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<EmailSubstitutions>());
    }

    [Fact]
    public async Task Submit_WhenTheDraftsExerciseIsOpen_SubmitsItAndNamesNoClosedExercise()
    {
        OneReadyDraft();
        SetupWindowDeadline();
        SetupRemoveDraft("R1");

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1" });

        Assert.Equal(new[] { "R1" }, result.Submitted);
        Assert.Null(result.ClosedExercise);
    }

    [Fact]
    public async Task Submit_AsksAboutTheWindowAsItIsNow_NotTheDraftsOwnSnapshot()
    {
        // The draft blob remembers the exercise dates from when the draft was saved. An exercise
        // closed early since then is still open in that snapshot, so the snapshot must not be asked.
        OneReadyDraft();
        var asItIsNow = PupilDataEnding(new DateTime(2026, 10, 1, 12, 34, 59));
        _pupilData.GetCheckingWindowAsync(WindowId).Returns(WindowWith(asItIsNow));
        SetupRemoveDraft("R1", snapshot: PupilDataEnding(FarFuture));
        _checkingExercises.IsOpen(default!, default).ReturnsForAnyArgs(ci =>
            ci.ArgAt<IReadOnlyList<CheckingExerciseDto>>(0).Any(e => e.EndDate == FarFuture));

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1" });

        Assert.Empty(result.Submitted);
        Assert.Equal(CheckingExerciseType.PupilData, result.ClosedExercise);
    }
}
