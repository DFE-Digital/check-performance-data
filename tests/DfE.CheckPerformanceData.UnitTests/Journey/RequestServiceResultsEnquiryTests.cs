using System.Text.RegularExpressions;
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
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: submitting a results enquiry.
//
// BA decision 2026-08-17: an enquiry drops into ChangeRequests and saves its journey JSON exactly as
// a pupil change request does — and is NOT enqueued. It is ultimately bound for Zendesk, but how it
// gets there is a separate story. The "never enqueues" assertions are the guard on that boundary:
// if enqueueing ever appears here it must be a deliberate change, not a copy-paste from the
// amendment path.
public sealed class RequestServiceResultsEnquiryTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChangeRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IQueueService _queue = Substitute.For<IQueueService>();
    private readonly IRequestNotificationService _notifications = Substitute.For<IRequestNotificationService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly RequestService _sut;

    public RequestServiceResultsEnquiryTests()
    {
        _currentUser.UserId.Returns(UserId.ToString());
        _currentUser.OrganisationUrn.Returns("142313");
        _currentUser.OrganisationLaestab.Returns("860/4070");
        _currentUser.DisplayName.Returns("Ada Editor");
        _currentUser.Email.Returns("ada@school.test");
        _repository.UpsertAsync(Arg.Any<ChangeRequestData>()).Returns(ChangeRequestId);

        _sut = new RequestService(
            _flowService, _stateBlob, _repository, _currentUser,
            NullLogger<RequestService>.Instance, _queue, _notifications, _pupilData,
            new CheckingExerciseService(TimeProvider.System));
    }

    private static StudentResultRecord Result() => new()
    {
        CypmdId = "1596410810", Qan = "60180882", QualificationName = "GCSE (9-1) Art&Des : Fine Art",
        SyllabusCode = "1AD0", Session = "S2024", Grade = "9", SourceFile = ResultsFileTags.Post16Main
    };

    private static RequestState Journey(bool cohortWide = true, string? additionalInfo = null) => new()
    {
        SelectedWhatToChange = WhatToChange.IncorrectGrade,
        CheckingWindow = new CheckingWindowDto
        {
            Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
        },
        ReferenceNumber = "CYPMD_16to19_RE_4F9C2A1",
        SelectedPupilId = Guid.NewGuid().ToString(),
        SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Firstname = "Billy", Surname = "B", Sex = "M", DateOfBirth = "12/03/2007",
            Age = 19, Cypmd_Id = "1596410810", Identifier = "9900000001"
        },
        SelectedResult = Result(),
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-cohort-scope"] = new() { TextValue = cohortWide ? "yes" : "no" },
            ["q-cohort-count"] = new() { TextValue = cohortWide ? "10" : null },
            ["q-revised-grade"] = new() { TextValue = "U" },
            ["q-additional-info"] = new() { TextValue = additionalInfo }
        },
        QuestionHistory = ["cohort-scope", "select-result", "grade-details", "additional-info"]
    };

    // ── The reference number ─────────────────────────────────────────────────

    [Fact]
    public void The_enquiry_reference_follows_the_agreed_format()
    {
        // Decided: CYPMD_16to19_RE_{7 hex}. The mockup's "3014023_RE10005" was confirmed illustrative.
        var reference = new JourneyValidationService().GenerateEnquiryReference();

        Assert.Matches(new Regex("^CYPMD_16to19_RE_[0-9A-F]{7}$"), reference);
    }

    [Fact]
    public void Enquiry_references_are_unique()
    {
        var service = new JourneyValidationService();

        var references = Enumerable.Range(0, 200).Select(_ => service.GenerateEnquiryReference()).ToHashSet();

        Assert.Equal(200, references.Count);
    }

    [Fact]
    public void An_enquiry_reference_is_distinguishable_from_an_amendment_reference()
    {
        // Support staff read these aloud; "RE" is how they tell an enquiry from an amendment.
        var service = new JourneyValidationService();

        Assert.Contains("_RE_", service.GenerateEnquiryReference());
        Assert.DoesNotContain("_RE_", service.GenerateReference(CheckingWindowType.Post16));
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Submitting_stores_a_results_enquiry_row()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.WindowId == WindowId &&
            d.ReferenceNumber == "CYPMD_16to19_RE_4F9C2A1" &&
            d.RequestType == RequestType.ResultsEnquiry &&
            d.AmendmentType == WhatToChange.IncorrectGrade &&
            d.Status == RequestStatus.SubmittedUnCommitted));
    }

    [Fact]
    public async Task The_row_carries_the_student_and_the_submitter()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.PupilId == Guid.Parse("33333333-3333-3333-3333-333333333333") &&
            d.PupilFirstname == "Billy" &&
            d.PupilSurname == "B" &&
            d.PupilUpn == "9900000001" &&
            d.OrganisationUrn == 142313L &&
            d.SubmittedById == UserId &&
            d.SubmittedByName == "Ada Editor" &&
            d.SubmittedByEmail == "ada@school.test"));
    }

    [Fact]
    public async Task The_row_describes_the_enquiry_in_words()
    {
        // RequestTypeDescription is what the admin lists show.
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.RequestTypeDescription == "Results enquiry - Incorrect grade"));
    }

    [Fact]
    public async Task The_timestamp_is_stored_as_the_wall_clock_kind_the_column_accepts()
    {
        // The column is `timestamp without time zone`; Npgsql rejects a Utc kind.
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _repository.Received(1).UpsertAsync(
            Arg.Is<ChangeRequestData>(d => d.Timestamp.Kind == DateTimeKind.Unspecified));
    }

    [Fact]
    public async Task Submitting_saves_the_journey_json_so_the_enquiry_can_be_rebuilt()
    {
        var journey = Journey();

        await _sut.SubmitResultsEnquiryAsync(WindowId, journey);

        await _stateBlob.Received(1).SaveAsync(WindowId, "CYPMD_16to19_RE_4F9C2A1",
            Arg.Is<RequestState>(s =>
                s.SelectedResult!.Qan == "60180882" &&
                s.SelectedResult.Grade == "9" &&
                s.QuestionAnswers["q-revised-grade"].TextValue == "U"));
    }

    [Fact]
    public async Task The_row_is_written_before_the_blob()
    {
        // The row is the record of truth; a blob with no row would be invisible to every admin view.
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        Received.InOrder(() =>
        {
            _repository.UpsertAsync(Arg.Any<ChangeRequestData>());
            _stateBlob.SaveAsync(WindowId, Arg.Any<string>(), Arg.Any<RequestState>());
        });
    }

    [Fact]
    public async Task Submitting_returns_the_reference_the_confirmation_page_shows()
    {
        var reference = await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        Assert.Equal("CYPMD_16to19_RE_4F9C2A1", reference);
    }

    // ── The parked boundary ──────────────────────────────────────────────────

    [Fact]
    public async Task Submitting_never_enqueues_anything()
    {
        // BA decision: Zendesk dispatch is a separate story. Nothing here may enqueue until it lands.
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!);
    }

    [Fact]
    public async Task Submitting_does_not_send_the_email_itself()
    {
        // The caller sends it, matching SubmitRequestAsync — so the journey controller can decide
        // whether a failure to email should fail the submission (it must not).
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _notifications.DidNotReceiveWithAnyArgs().NotifySubmissionConfirmedAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task Submitting_does_not_run_the_duplicate_check()
    {
        // The spec allows several enquiries for the same pupil and result — a school may be reporting
        // more than one thing about the same qualification.
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey());

        await _repository.DidNotReceiveWithAnyArgs()
            .CheckForConflictAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task Two_enquiries_for_the_same_result_both_persist()
    {
        var first = Journey();
        var second = Journey();
        second.ReferenceNumber = "CYPMD_16to19_RE_BBBBBB2";

        await _sut.SubmitResultsEnquiryAsync(WindowId, first);
        await _sut.SubmitResultsEnquiryAsync(WindowId, second);

        await _repository.Received(2).UpsertAsync(Arg.Any<ChangeRequestData>());
    }

    // ── AB#297848: the missing-qualification sibling enquiry ─────────────────

    private static RequestState MissingQualificationJourney() => new()
    {
        SelectedWhatToChange = WhatToChange.MissingQualification,
        CheckingWindow = new CheckingWindowDto
        {
            Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
        },
        ReferenceNumber = "CYPMD_16to19_RE_1A2B3C4",
        SelectedPupilId = Guid.NewGuid().ToString(),
        SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Firstname = "Billy", Surname = "B", Sex = "M", DateOfBirth = "12/03/2007",
            Age = 19, Cypmd_Id = "500001", Identifier = "9900000001"
        },
        SelectedQualification = new QualificationReference
        {
            Qan = "60146084", QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
            AwardingOrganisation = "AQA", Grades = ["1", "2", "3"],
            SyllabusCodes = [new SyllabusCode { Code = "8300H", Title = "Mathematics Higher Tier" }]
        },
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-syllabus-code"] = new() { TextValue = "8300H" },
            ["q-missing-grade"] = new() { TextValue = "2" }
        },
        QuestionHistory = ["select-qualification", "qualification-details", "additional-info"]
    };

    [Fact]
    public async Task A_missing_qualification_enquiry_persists_with_its_own_amendment_type_and_description()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, MissingQualificationJourney());

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.AmendmentType == WhatToChange.MissingQualification &&
            d.RequestTypeDescription == "Results enquiry - Missing qualification" &&
            d.Status == RequestStatus.SubmittedUnCommitted &&
            d.RequestType == RequestType.ResultsEnquiry));
    }

    [Fact]
    public async Task A_missing_qualification_enquiry_never_enqueues()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, MissingQualificationJourney());

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!);
    }

    [Fact]
    public async Task Submission_requires_a_selected_qualification()
    {
        var journey = MissingQualificationJourney();
        journey.SelectedQualification = null;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SubmitResultsEnquiryAsync(WindowId, journey));
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!);
    }

    [Fact]
    public async Task A_missing_qualification_enquiry_does_not_require_a_selected_result()
    {
        // SelectedResult is the incorrect-grade path's subject; a missing qualification has none.
        var journey = MissingQualificationJourney();
        Assert.Null(journey.SelectedResult);

        var reference = await _sut.SubmitResultsEnquiryAsync(WindowId, journey);

        Assert.Equal("CYPMD_16to19_RE_1A2B3C4", reference);
    }

    [Fact]
    public async Task An_amendment_still_cannot_route_through_the_enquiry_path()
    {
        var journey = MissingQualificationJourney();
        journey.SelectedWhatToChange = WhatToChange.Remove;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SubmitResultsEnquiryAsync(WindowId, journey));
    }

    // ── AB#298704: the result-does-not-belong sibling enquiry ────────────────

    private static RequestState ResultDoesNotBelongJourney() => new()
    {
        SelectedWhatToChange = WhatToChange.ResultDoesNotBelong,
        CheckingWindow = new CheckingWindowDto
        {
            Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
        },
        ReferenceNumber = "CYPMD_16to19_RE_9D8E7F6",
        SelectedPupilId = Guid.NewGuid().ToString(),
        SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Firstname = "Billy", Surname = "B", Sex = "M", DateOfBirth = "12/03/2007",
            Age = 19, Cypmd_Id = "1596410810", Identifier = "9900000001"
        },
        SelectedResult = Result(),
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-additional-info"] = new() { TextValue = "QAN 60322222 grade B also does not belong" }
        },
        QuestionHistory = ["select-student", "select-result", "additional-info"]
    };

    [Fact]
    public async Task Submitting_a_result_does_not_belong_enquiry_persists_the_row_with_its_own_description()
    {
        // AB#298704: third enquiry kind. Subject is the selected result (like incorrect grade,
        // unlike missing qualification), and the description is the support-facing label.
        var journey = ResultDoesNotBelongJourney();

        await _sut.SubmitResultsEnquiryAsync(WindowId, journey);

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.RequestType == RequestType.ResultsEnquiry &&
            d.RequestTypeDescription == "Results enquiry - Result does not belong to student" &&
            d.AmendmentType == WhatToChange.ResultDoesNotBelong &&
            d.Status == RequestStatus.SubmittedUnCommitted));
    }

    [Fact]
    public async Task A_result_does_not_belong_enquiry_without_a_selected_result_is_rejected()
    {
        var journey = ResultDoesNotBelongJourney();
        journey.SelectedResult = null;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SubmitResultsEnquiryAsync(WindowId, journey));
    }

    // ── Guard rails ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("window")]
    [InlineData("pupil")]
    [InlineData("result")]
    [InlineData("reference")]
    public async Task An_incomplete_journey_is_refused_rather_than_half_written(string missing)
    {
        var journey = Journey();
        switch (missing)
        {
            case "window": journey.CheckingWindow = null; break;
            case "pupil": journey.SelectedPupil = null; break;
            case "result": journey.SelectedResult = null; break;
            case "reference": journey.ReferenceNumber = null; break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SubmitResultsEnquiryAsync(WindowId, journey));
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!);
        await _stateBlob.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_journey_for_a_different_change_type_is_refused()
    {
        // This method is the enquiry path only; routing an amendment through it would store the wrong
        // RequestType and silently bypass the rules engine.
        var journey = Journey();
        journey.SelectedWhatToChange = WhatToChange.Remove;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SubmitResultsEnquiryAsync(WindowId, journey));
    }

    [Fact]
    public async Task The_single_pupil_branch_submits_without_a_cohort_count()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey(cohortWide: false));

        await _repository.Received(1).UpsertAsync(Arg.Any<ChangeRequestData>());
        await _stateBlob.Received(1).SaveAsync(WindowId, Arg.Any<string>(), Arg.Any<RequestState>());
    }

    [Fact]
    public async Task Comments_are_optional()
    {
        await _sut.SubmitResultsEnquiryAsync(WindowId, Journey(additionalInfo: null));

        await _stateBlob.Received(1).SaveAsync(WindowId, Arg.Any<string>(), Arg.Any<RequestState>());
    }
}
