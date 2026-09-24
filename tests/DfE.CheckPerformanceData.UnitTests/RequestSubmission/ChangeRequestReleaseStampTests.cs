using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.RequestSubmission;

// A change request records the exercise release that was live when it was written: the data the
// school was looking at. A later release may replace that data, and a reviewer needs to know which
// data the request was made against.
public sealed class ChangeRequestReleaseStampTests
{
    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly Guid ExerciseId = Guid.NewGuid();
    private static readonly Guid LiveReleaseId = Guid.NewGuid();

    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ICheckingExerciseStorageResolver _resolver = Substitute.For<ICheckingExerciseStorageResolver>();

    public ChangeRequestReleaseStampTests()
    {
        _currentUser.UserId.Returns(Guid.NewGuid().ToString());
        _currentUser.OrganisationUrn.Returns("142313");
        _currentUser.DisplayName.Returns("Ada Editor");
        _currentUser.Email.Returns("ada@school.test");
    }

    private RequestService Service(ICheckingExerciseStorageResolver? resolver) => new(
        Substitute.For<IQuestionFlowService>(), Substitute.For<IRequestStateBlobClient>(), _repository, _currentUser,
        NullLogger<RequestService>.Instance, Substitute.For<IQueueService>(),
        Substitute.For<IRequestNotificationService>(), Substitute.For<ICheckYourPupilDataService>(),
        new CheckingExerciseService(TimeProvider.System), resolver);

    private static CheckingExerciseDto Exercise(Guid? currentRelease) => new()
    {
        Id = ExerciseId, ExerciseType = CheckingExerciseType.PupilData,
        StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
        CurrentReleaseId = currentRelease
    };

    private static RequestState Journey() => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId, Title = "KS4", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
            Exercises = [Exercise(null)]
        },
        ReferenceNumber = "CYPMD_KS4_ABC1234",
        QuestionHistory = [],
        QuestionAnswers = new Dictionary<string, QuestionAnswer>(),
        SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M", DateOfBirth = "12/03/2010",
            Age = 16, Cypmd_Id = "1", Identifier = "A1"
        }
    };

    [Fact]
    public async Task A_draft_is_stamped_with_the_release_live_when_it_is_saved()
    {
        // The session's copy of the window is from the start of the journey and says no release.
        // The stamp comes from the release live now, as the journey's data readers see it.
        _resolver.ResolveAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(Exercise(LiveReleaseId));
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await Service(_resolver).SaveDraftAsync(WindowId, Journey(), RequestStatus.InProgress);

        Assert.Equal(LiveReleaseId, captured!.CheckingExerciseReleaseId);
    }

    [Fact]
    public async Task A_request_on_an_exercise_with_no_release_has_no_release_stamp()
    {
        _resolver.ResolveAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(Exercise(null));
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await Service(_resolver).SaveDraftAsync(WindowId, Journey(), RequestStatus.InProgress);

        Assert.Null(captured!.CheckingExerciseReleaseId);
    }

    [Fact]
    public async Task With_no_resolver_the_request_is_still_written()
    {
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await Service(null).SaveDraftAsync(WindowId, Journey(), RequestStatus.InProgress);

        Assert.Equal(ExerciseId, captured!.CheckingExerciseId);
        Assert.Null(captured.CheckingExerciseReleaseId);
    }
}
