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
    private readonly BulkSubmissionService _sut;

    public BulkSubmissionServiceTests()
    {
        _currentUser.OrganisationUrn.Returns(Urn.ToString());
        _sut = new BulkSubmissionService(_repo, _requestService, _notify, _pupilData, _currentUser, _analytics);
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
            WindowId, Arg.Any<DateTime>(), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "R1", "R2" })));
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
            .Returns(Task.FromException(new DuplicateRequestException()));

        var result = await _sut.SubmitAsync(WindowId, new[] { "R1", "R2" });

        Assert.Equal(new[] { "R2" }, result.Submitted);
        Assert.Equal(new[] { "R1" }, result.Skipped);
        await _notify.Received(1).NotifyBulkSubmissionConfirmedAsync(
            WindowId, Arg.Any<DateTime>(), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "R2" })));
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
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<string>>());
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
            .Returns(Task.FromException(new DuplicateRequestException())); // R1 conflicts, only R2 submits

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
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<string>>());
    }
}
