using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;

namespace DfE.CheckPerformanceData.Application.UnitTests.RequestSubmission;

// Every write of a ChangeRequests row stamps the CheckingExercises row it belongs to, so an admin
// can group the requests by exercise and ask the open/closed question of the right one. WindowId
// alone cannot answer either question on a window that runs pupil data checking and a results
// enquiry on different date ranges (#319).
//
// The exercise is never read off the session: it is derived from the journey's own
// SelectedWhatToChange through WhatToChangeCheckingExerciseMap, then resolved to a row id against
// the window's exercises. These tests pin that derivation at each of the three write sites.
public sealed class ChangeRequestCheckingExerciseStampTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PupilDataExerciseId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ResultsEnquiryExerciseId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly RequestService _sut;

    public ChangeRequestCheckingExerciseStampTests()
    {
        _currentUser.UserId.Returns("11111111-1111-1111-1111-111111111111");
        _currentUser.OrganisationUrn.Returns("142313");
        _currentUser.DisplayName.Returns("Ada Editor");
        _currentUser.Email.Returns("ada@school.test");

        _sut = new RequestService(
            _flowService, _stateBlob, _repository, _currentUser,
            NullLogger<RequestService>.Instance, Substitute.For<IQueueService>(),
            Substitute.For<IRequestNotificationService>(), _pupilData,
            new CheckingExerciseService(TimeProvider.System));
    }

    // A 16-19 window: both exercises, on ranges that do not coincide.
    private static List<CheckingExerciseDto> BothExercises() =>
    [
        new()
        {
            Id = PupilDataExerciseId,
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 11, 30),
            SortOrder = 0
        },
        new()
        {
            Id = ResultsEnquiryExerciseId,
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 3, 31),
            SortOrder = 1
        }
    ];

    private static CheckingWindowDto Window(List<CheckingExerciseDto>? exercises = null) => new()
    {
        Id = WindowId,
        Title = "16 to 19 2026",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1),
        EndDate = new DateTime(2027, 3, 31),
        Exercises = exercises ?? BothExercises()
    };

    private static RequestState Journey(WhatToChange change, List<CheckingExerciseDto>? exercises = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = change,
            CheckingWindow = Window(exercises),
            ReferenceNumber = "CYPMD_16to19_ABC1234",
            QuestionHistory = [],
            QuestionAnswers = new Dictionary<string, QuestionAnswer>()
        };
        state.SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Firstname = "Billy",
            Surname = "B",
            Sex = "M",
            DateOfBirth = "12/03/2007",
            Age = 19,
            Cypmd_Id = "1596410810",
            Identifier = "9900000001"
        };
        return state;
    }

    [Fact]
    public async Task An_amendment_is_stamped_with_the_pupil_data_exercise()
    {
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await _sut.SaveDraftAsync(WindowId, Journey(WhatToChange.Remove), RequestStatus.InProgress);

        Assert.Equal(PupilDataExerciseId, captured!.CheckingExerciseId);
    }

    [Fact]
    public async Task A_results_enquiry_is_stamped_with_the_results_enquiry_exercise()
    {
        // The same window, the same school, a different exercise — which is the whole point of the
        // column: WindowId is identical on both rows.
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        var journey = Journey(WhatToChange.IncorrectGrade);
        journey.SelectedResult = new StudentResultRecord
        {
            CypmdId = "1596410810",
            Qan = "60180882",
            QualificationName = "GCSE (9-1) Art&Des : Fine Art",
            SyllabusCode = "1AD0",
            Session = "S2024",
            Grade = "9",
            SourceFile = ResultsFileTags.Post16Main
        };

        await _sut.SubmitResultsEnquiryAsync(WindowId, journey);

        Assert.Equal(ResultsEnquiryExerciseId, captured!.CheckingExerciseId);
    }

    [Fact]
    public async Task A_confirm_correct_declaration_is_stamped_with_the_pupil_data_exercise()
    {
        // A declaration has no journey and no AmendmentType, so nothing about the row would say
        // which exercise it belongs to if this write site skipped the stamp.
        _pupilData.GetCheckingWindowAsync(WindowId).Returns(Window());
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await _sut.ConfirmDataCorrectAsync(
            WindowId, "CYPMD_16to19_DEF5678", new DateTime(2026, 11, 30),
            new EmailSubstitutions("16 to 19 2026", "Student", ""));

        Assert.Equal(PupilDataExerciseId, captured!.CheckingExerciseId);
    }

    [Fact]
    public async Task The_stamp_is_null_when_the_window_has_no_row_for_that_exercise()
    {
        // Fails closed the same way ICheckingExerciseService does everywhere else: a
        // half-configured window leaves the column null rather than guessing at a neighbour.
        List<CheckingExerciseDto> enquiryOnly = [BothExercises()[1]];
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await _sut.SaveDraftAsync(
            WindowId, Journey(WhatToChange.Remove, enquiryOnly), RequestStatus.InProgress);

        Assert.Null(captured!.CheckingExerciseId);
    }

    [Fact]
    public async Task The_stamp_survives_the_exercise_having_closed()
    {
        // Every exercise on this window ended months ago. A draft saved (or a submitted request
        // re-upserted) after the close must still say which exercise it belongs to — an admin
        // grouping past requests is exactly the reader that needs it.
        List<CheckingExerciseDto> closed =
        [
            new()
            {
                Id = PupilDataExerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2020, 1, 1),
                EndDate = new DateTime(2020, 2, 1),
                SortOrder = 0
            }
        ];
        ChangeRequestData? captured = null;
        _repository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await _sut.SaveDraftAsync(
            WindowId, Journey(WhatToChange.Remove, closed), RequestStatus.InProgress);

        Assert.Equal(PupilDataExerciseId, captured!.CheckingExerciseId);
    }
}
