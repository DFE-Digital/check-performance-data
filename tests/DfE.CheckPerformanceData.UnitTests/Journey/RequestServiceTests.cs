using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class RequestServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IRequestStateBlobClient _requestStateBlobClient = Substitute.For<IRequestStateBlobClient>();
    private readonly IRequestRepository _requestRepository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IRequestBlobClient _requestBlobClient = Substitute.For<IRequestBlobClient>();
    private readonly ILogger<RequestService> _logger = Substitute.For<ILogger<RequestService>>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IRequestNotificationService _requestNotificationService = Substitute.For<IRequestNotificationService>();
    private readonly ICheckYourPupilDataService _checkYourPupilDataService = Substitute.For<ICheckYourPupilDataService>();
    private readonly RequestService _sut;

    public RequestServiceTests()
    {
        _currentUser.UserId.Returns("11111111-1111-1111-1111-111111111111");
        _currentUser.DisplayName.Returns("Test User");
        _currentUser.Email.Returns("test.user@education.gov.uk");
        _currentUser.OrganisationId.Returns("00000000-0000-0000-0000-000000000001");
        _currentUser.OrganisationUrn.Returns("100000");
        _currentUser.Ukprn.Returns("10000000");
        _currentUser.OrganisationName.Returns("Test School");
        _sut = new RequestService(_flowService, _requestStateBlobClient, _requestRepository, _currentUser, _logger, _queueService, _requestNotificationService, _checkYourPupilDataService);
    }

    // ── ConfirmRequestAsync — guard checks ──────────────────────────────────

    [Fact]
    public async Task ConfirmRequestAsync_WhenSessionIncomplete_Throws()
    {
        var journey = new RequestState(); // no WhatToChange, CheckingWindow, SelectedPupil

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConfirmRequestAsync(WindowId, journey));
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenConfigNotFound_Throws()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns((QuestionFlowConfig?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConfirmRequestAsync(WindowId, ValidJourney()));
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenSelfSubmittedConflict_ThrowsDuplicateRequestException()
    {
        var journey = ValidJourney();
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _requestRepository
            .CheckForConflictAsync(
                WindowId,
                journey.SelectedPupil!.Id,
                100000L,
                journey.ReferenceNumber!,
                userId)
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF-CONFLICT", ""));

        await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            _sut.ConfirmRequestAsync(WindowId, journey));

        await _queueService.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<RequestDocument>());
        await _requestRepository.DidNotReceive().UpsertAsync(Arg.Any<ChangeRequestData>());
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenNoConflictingRequest_DoesNotThrow()
    {
        _requestRepository
            .CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.NoConflict());
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        // Should complete without throwing
        await _sut.ConfirmRequestAsync(WindowId, journey);
    }

    // ── Document building ───────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmRequestAsync_MapsUserAndSchoolDetails()
    {
        var (journey, config) = MakeSubmission();

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("11111111-1111-1111-1111-111111111111", doc.SubmittedBy.UserId);
        Assert.Equal("Test User", doc.SubmittedBy.DisplayName);
        Assert.Equal("100000", doc.School.Urn);
        Assert.Equal("Test School", doc.School.Name);
    }

    [Fact]
    public async Task ConfirmRequestAsync_MapsPupilDetails()
    {
        var (journey, config) = MakeSubmission();

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Jane", doc.Pupil.Firstname);
        Assert.Equal("Smith", doc.Pupil.Surname);
        Assert.Equal("F", doc.Pupil.Sex);
    }

    [Fact]
    public async Task ConfirmRequestAsync_MapsPupilPincl()
    {
        // The rules engine derives inclusionFlag from the pupil record's Pincl —
        // it is never asked as a journey question.
        var (journey, config) = MakeSubmission();
        journey.SelectedPupil!.Pincl = 402;

        var doc = await CaptureDocument(journey, config);

        Assert.Equal(402, doc.Pupil.Pincl);
    }

    [Fact]
    public async Task ConfirmRequestAsync_SavesChangeRequestToRepository()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _requestRepository.Received(1).UpsertAsync(Arg.Any<ChangeRequestData>());
    }

    [Fact]
    public async Task ConfirmRequestAsync_UpsertsChangeRequestBeforeEnqueue()
    {
        // The document must carry the ChangeRequest row's Id, so the row has to
        // exist before the document is enqueued.
        var (journey, config) = MakeSubmission();
        SetupConfig(config);
        var callOrder = new List<string>();
        _queueService.EnqueueAsync(Arg.Any<string>(), Arg.Any<RequestDocument>())
            .Returns(_ => { callOrder.Add("enqueue"); return Guid.NewGuid(); });
        _requestRepository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(_ => { callOrder.Add("db"); return Guid.NewGuid(); });

        await _sut.ConfirmRequestAsync(WindowId, journey);

        Assert.Equal(["db", "enqueue"], callOrder);
    }

    [Fact]
    public async Task ConfirmRequestAsync_DocumentCarriesChangeRequestIdReturnedByUpsert()
    {
        var changeRequestId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        _requestRepository.UpsertAsync(Arg.Any<ChangeRequestData>()).Returns(changeRequestId);
        var (journey, config) = MakeSubmission();

        var doc = await CaptureDocument(journey, config);

        Assert.Equal(changeRequestId, doc.ChangeRequestId);
    }

    [Fact]
    public async Task ConfirmRequestAsync_IncludesAnswersForQuestionPages()
    {
        var config = MakeConfig([
            new JourneyPage { Id = "reason", Questions = [MakeQuestion(QuestionType.Radio, id: "reason")] }
        ]);
        var journey = ValidJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new QuestionAnswer { TextValue = "opted-out" } });

        var doc = await CaptureDocument(journey, config);

        Assert.Single(doc.Answers);
        Assert.Equal("reason", doc.Answers[0].QuestionId);
    }

    [Fact]
    public async Task ConfirmRequestAsync_ExcludesContentPages()
    {
        var config = MakeConfig([
            new JourneyPage { Id = "info", Type = PageType.Content },
            new JourneyPage { Id = "reason", Questions = [MakeQuestion(QuestionType.Radio)] }
        ]);
        var journey = ValidJourney(history: ["info", "reason"]);

        var doc = await CaptureDocument(journey, config);

        Assert.All(doc.Answers, a => Assert.NotEqual("info", a.QuestionId));
    }

    [Fact]
    public async Task ConfirmRequestAsync_ExcludesPupilSearchPages()
    {
        var config = MakeConfig([
            new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch, PupilKey = JourneyPage.PrimaryKey },
            new JourneyPage { Id = "reason", Questions = [MakeQuestion(QuestionType.Radio, id: "reason")] }
        ]);
        var journey = ValidJourney(history: ["select-pupil", "reason"]);

        var doc = await CaptureDocument(journey, config);

        Assert.All(doc.Answers, a => Assert.NotEqual("select-pupil", a.QuestionId));
        Assert.Single(doc.Answers);
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenMatchedPupilSet_PopulatesMatchedPupilInDocument()
    {
        var (journey, config) = MakeSubmission();
        journey.MatchedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "John",
            Surname = "Doe",
            Sex = "M",
            DateOfBirth = "02/02/2010",
            Age = 16,
            Cypmd_Id = "CYPMD456",
            Upn = "456456",
            Pincl = 402
        };
        journey.MatchedPupilId = journey.MatchedPupil.Id.ToString();

        var doc = await CaptureDocument(journey, config);

        Assert.NotNull(doc.MatchedPupil);
        Assert.Equal("John", doc.MatchedPupil.Firstname);
        Assert.Equal("Doe", doc.MatchedPupil.Surname);
        Assert.Equal("456456", doc.MatchedPupil.Upn);
        // The rules engine needs Pincl on the matched pupil too, not just the selected pupil.
        Assert.Equal(402, doc.MatchedPupil.Pincl);
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenNoMatchedPupil_MatchedPupilIsNullInDocument()
    {
        var (journey, config) = MakeSubmission();
        // MatchedPupil is not set (Remove journey)

        var doc = await CaptureDocument(journey, config);

        Assert.Null(doc.MatchedPupil);
    }

    [Fact]
    public async Task ConfirmRequestAsync_RadioAnswer_ResolvesLabel()
    {
        var question = new Question
        {
            Id = "reason",
            Type = QuestionType.Radio,
            Title = "Reason",
            Options = [new QuestionOption { Value = "opt-1", Label = "Opted Out" }]
        };
        var config = MakeConfig([new JourneyPage { Id = "reason", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new QuestionAnswer { TextValue = "opt-1" } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Opted Out", doc.Answers[0].Value);
    }

    [Fact]
    public async Task ConfirmRequestAsync_DateAnswer_FormatsAsDDMMYYYY()
    {
        var question = MakeQuestion(QuestionType.Date, id: "dob");
        var config = MakeConfig([new JourneyPage { Id = "dob", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["dob"],
            answers: new() { ["dob"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 5, Month = 3, Year = 2010 } } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("05/03/2010", doc.Answers[0].Value);
    }

    [Fact]
    public async Task ConfirmRequestAsync_RadioAnswer_RawValueIsOptionValue()
    {
        var question = new Question
        {
            Id = "reason",
            Type = QuestionType.Radio,
            Title = "Reason",
            Options = [new QuestionOption { Value = "opt-1", Label = "Opted Out" }]
        };
        var config = MakeConfig([new JourneyPage { Id = "reason", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new QuestionAnswer { TextValue = "opt-1" } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("opt-1", doc.Answers[0].RawValue);
        Assert.Equal("Opted Out", doc.Answers[0].Value);
    }

    [Fact]
    public async Task ConfirmRequestAsync_DateAnswer_RawValueIsIsoDate()
    {
        var question = MakeQuestion(QuestionType.Date, id: "dob");
        var config = MakeConfig([new JourneyPage { Id = "dob", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["dob"],
            answers: new() { ["dob"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 5, Month = 3, Year = 2010 } } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("2010-03-05", doc.Answers[0].RawValue);
        Assert.Equal("05/03/2010", doc.Answers[0].Value);
    }

    [Fact]
    public async Task ConfirmRequestAsync_AutocompleteAnswer_RawValueIsCode()
    {
        var question = MakeQuestion(QuestionType.Autocomplete, id: "country");
        var config = MakeConfig([new JourneyPage { Id = "country", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["country"],
            answers: new() { ["country"] = new QuestionAnswer { TextValue = "France", CodeValue = "FR" } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("FR", doc.Answers[0].RawValue);
        Assert.Equal("France", doc.Answers[0].Value);
    }

    [Fact]
    public async Task ConfirmRequestAsync_FreeTextAnswer_RawValueIsText()
    {
        var question = MakeQuestion(QuestionType.FreeText, id: "notes");
        var config = MakeConfig([new JourneyPage { Id = "notes", Questions = [question] }]);
        var journey = ValidJourney(
            history: ["notes"],
            answers: new() { ["notes"] = new QuestionAnswer { TextValue = "Some detail" } });

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Some detail", doc.Answers[0].RawValue);
    }

    [Fact]
    public async Task ConfirmRequestAsync_PupilNameTemplate_IsResolved()
    {
        var question = new Question { Id = "q1", Type = QuestionType.FreeText, Title = "Notes for {pupilName}" };
        var config = MakeConfig([new JourneyPage { Id = "p1", Questions = [question] }]);
        var journey = ValidJourney(history: ["p1"]);

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Notes for Jane Smith", doc.Answers[0].QuestionTitle);
    }

    [Fact]
    public async Task ConfirmRequestAsync_RequestTypeCode_IncludesReasonValue_WhenFlowResolvesOne()
    {
        var (journey, config) = MakeSubmission();
        _flowService.ResolveRequestTypeValue(config, Arg.Any<RequestState>()).Returns("pupil-died");

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Remove - pupil-died", doc.RequestTypeCode);
    }

    [Fact]
    public async Task ConfirmRequestAsync_RequestTypeCode_IsBarePrefix_WhenFlowResolvesNoValue()
    {
        var (journey, config) = MakeSubmission();
        _flowService.ResolveRequestTypeValue(config, Arg.Any<RequestState>()).Returns(string.Empty);

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Remove", doc.RequestTypeCode);
    }

    [Fact]
    public async Task ConfirmRequestAsync_SendsDocumentToRulesEngineQueue()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _queueService.Received(1).EnqueueAsync(QueueOptions.RulesEngineQueue, Arg.Any<RequestDocument>());
    }

    [Fact]
    public async Task ConfirmRequestAsync_StampsRowWithSubmitterEmailThenPersistsJourney()
    {
        _currentUser.Email.Returns("submitter@education.gov.uk");
        var (journey, config) = MakeSubmission();
        SetupConfig(config);
        ChangeRequestData? captured = null;
        _requestRepository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d)).Returns(Guid.NewGuid());

        await _sut.ConfirmRequestAsync(WindowId, journey);

        // The ChangeRequests row is the single source of truth for the submitter email/time.
        Assert.Equal("submitter@education.gov.uk", captured!.SubmittedByEmail);
        await _requestStateBlobClient.Received(1).SaveAsync(WindowId, journey.ReferenceNumber!, journey);
    }

    // ── SaveDraftAsync ──────────────────────────────────────────────────────

    [Fact]
    public void RequestStatus_HasInProgressAndReadyToSubmitValues()
    {
        Assert.True(Enum.IsDefined(typeof(RequestStatus), RequestStatus.InProgress));
        Assert.True(Enum.IsDefined(typeof(RequestStatus), RequestStatus.ReadyToSubmit));
    }

    [Fact]
    public async Task SaveDraftAsync_WhenSessionIncomplete_Throws()
    {
        var journey = new RequestState(); // missing required fields

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress));
    }

    [Fact]
    public async Task SaveDraftAsync_WhenReferenceNumberMissing_Throws()
    {
        var journey = ValidJourney();
        journey.ReferenceNumber = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress));
    }

    [Fact]
    public async Task SaveDraftAsync_SavesBlobWithCorrectWindowIdAndReferenceNumber()
    {
        var journey = ValidJourney();

        await _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress);

        await _requestStateBlobClient.Received(1).SaveAsync(WindowId, journey.ReferenceNumber!, journey);
    }

    [Fact]
    public async Task SaveDraftAsync_UpsertsRecordWithPassedStatusAndCorrectReferenceNumber()
    {
        var journey = ValidJourney();

        await _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress);

        await _requestRepository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.ReferenceNumber == journey.ReferenceNumber &&
            d.Status == RequestStatus.InProgress));
    }

    [Fact]
    public async Task SaveDraftAsync_MapsCorrectPupilAndSchoolDetails()
    {
        var journey = ValidJourney();
        ChangeRequestData? captured = null;
        _requestRepository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d));

        await _sut.SaveDraftAsync(WindowId, journey, RequestStatus.ReadyToSubmit);

        Assert.Equal("Jane", captured!.PupilFirstname);
        Assert.Equal("Smith", captured.PupilSurname);
        Assert.Equal(journey.SelectedPupil!.Id, captured.PupilId);
        Assert.Equal("123123", captured.PupilUpn);
        Assert.Equal(100000L, captured.OrganisationUrn);
        Assert.Equal("Test User", captured.SubmittedByName);
        Assert.Equal(RequestStatus.ReadyToSubmit, captured.Status);
        Assert.Equal(RequestType.Amendment, captured.RequestType);
        // Config is null in this test so the description falls back to the WhatToChange prefix only
        Assert.Equal("Remove", captured.RequestTypeDescription);
    }

    [Theory]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ReadyToSubmit)]
    public async Task SaveDraftAsync_ForwardsStatusToRepository(RequestStatus status)
    {
        var journey = ValidJourney();

        await _sut.SaveDraftAsync(WindowId, journey, status);

        await _requestRepository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.Status == status));
    }

    [Fact]
    public async Task SaveDraftAsync_RequestTypePrefixedWithWhatToChange()
    {
        var journey = ValidJourney();
        var config = MakeConfig([new JourneyPage { Id = "reason", Questions = [
            new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                UseAsRequestType = true,
                Options = [new QuestionOption { Value = "perm-ex", Label = "Permanently excluded" }] }
        ]}]);
        journey.QuestionHistory = ["reason"];
        journey.QuestionAnswers["reason"] = new QuestionAnswer { TextValue = "perm-ex" };
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(config);
        _flowService.ResolveRequestType(config, Arg.Any<RequestState>()).Returns("Permanently excluded");
        ChangeRequestData? captured = null;
        _requestRepository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d));

        await _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress);

        Assert.Equal(RequestType.Amendment, captured!.RequestType);
        Assert.Equal("Remove - Permanently excluded", captured.RequestTypeDescription);
    }

    [Fact]
    public async Task ConfirmDataCorrectAsync_WritesConfirmCorrectRequestType()
    {
        ChangeRequestData? captured = null;
        _requestRepository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d));

        await _sut.ConfirmDataCorrectAsync(WindowId, "REF999", new DateTime(2026, 6, 26, 17, 0, 0));

        Assert.Equal(RequestType.ConfirmCorrect, captured!.RequestType);
        Assert.Equal("Confirm Pupil Data Declaration", captured.RequestTypeDescription);
        Assert.Equal("test.user@education.gov.uk", captured.SubmittedByEmail);
    }

    [Fact]
    public async Task SaveDraftAsync_SavesBlobBeforeUpsertingRecord()
    {
        var journey = ValidJourney();
        var callOrder = new List<string>();
        _requestStateBlobClient.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<RequestState>())
            .Returns(_ => { callOrder.Add("blob"); return Task.CompletedTask; });
        _requestRepository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(_ => { callOrder.Add("db"); return Guid.NewGuid(); });

        await _sut.SaveDraftAsync(WindowId, journey, RequestStatus.InProgress);

        Assert.Equal(["blob", "db"], callOrder);
    }

    // ── ResumeDraftAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeDraftAsync_ReturnsStateFromBlobClient()
    {
        StubOwningRow("REF001");
        var expected = new RequestState { ReferenceNumber = "REF001" };
        _requestStateBlobClient.GetAsync(WindowId, "REF001").Returns(expected);

        var result = await _sut.ResumeDraftAsync(WindowId, "REF001");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ResumeDraftAsync_WhenDraftNotFound_ReturnsNull()
    {
        StubOwningRow("MISSING");
        _requestStateBlobClient.GetAsync(WindowId, "MISSING").Returns((RequestState?)null);

        var result = await _sut.ResumeDraftAsync(WindowId, "MISSING");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResumeDraftAsync_WhenCallerOrgDoesNotOwnDraft_ReturnsNullWithoutReadingBlob()
    {
        // No ChangeRequests row for this org/reference → cross-tenant access attempt.
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, "REF001")
            .Returns((AmendmentRequestData?)null);

        var result = await _sut.ResumeDraftAsync(WindowId, "REF001");

        Assert.Null(result);
        await _requestStateBlobClient.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ── ConfirmRequestAsync — recipient routing ─────────────────────────────

    [Fact]
    public async Task ConfirmRequestAsync_DelegatesSubmissionNotification()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _requestNotificationService.Received(1).NotifySubmissionConfirmedAsync(
            WindowId, journey.CheckingWindow.EndDate, journey.ReferenceNumber);
    }

    // ── ConfirmDataCorrectAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ConfirmDataCorrectAsync_DelegatesNotification()
    {
        var refNum = "CYPMD_KS4June_ABC1234";
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);

        await _sut.ConfirmDataCorrectAsync(WindowId, refNum, endDate);

        await _requestNotificationService.Received(1).NotifyDataCheckConfirmedAsync(endDate, refNum);
    }

    [Fact]
    public async Task ResumeDraftAsync_PassesWindowIdAndReferenceNumberToBlobClient()
    {
        StubOwningRow("REF001");
        _requestStateBlobClient.GetAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns((RequestState?)null);

        await _sut.ResumeDraftAsync(WindowId, "REF001");

        await _requestStateBlobClient.Received(1).GetAsync(WindowId, "REF001");
    }

    // Stubs a ChangeRequests row owned by the test's organisation (URN 100000) so the
    // org-scoping gate in ResumeDraftAsync passes and the blob read is reached.
    private void StubOwningRow(string referenceNumber) =>
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, referenceNumber)
            .Returns(new AmendmentRequestData
            {
                PupilFirstname = "Jane",
                PupilSurname = "Smith",
                RequestType = RequestType.Amendment,
                RequestTypeDescription = "Remove",
                Status = RequestStatus.InProgress,
                ReferenceNumber = referenceNumber
            });

    // ── DeleteAsync ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ReadyToSubmit)]
    public async Task DeleteAsync_WhenDraft_HardDeletesRowAndBlob(RequestStatus status)
    {
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, "REF001")
            .Returns(AmendmentRow(status, "Jane", "Smith"));

        var result = await _sut.DeleteAsync(WindowId, "REF001");

        Assert.True(result.WasHardDeleted);
        Assert.Equal("Jane Smith", result.PupilName);
        await _requestRepository.Received(1).DeleteAsync(WindowId, 100000L, "REF001");
        await _requestStateBlobClient.Received(1).DeleteAsync(WindowId, "REF001");
        await _requestRepository.DidNotReceive().WithdrawAsync(WindowId, 100000L, "REF001");
    }

    [Fact]
    public async Task DeleteAsync_WhenSubmitted_WithdrawsWithoutHardDelete()
    {
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, "REF001")
            .Returns(AmendmentRow(RequestStatus.SubmittedUnCommitted, "Jane", "Smith"));
        _checkYourPupilDataService.GetCheckingWindowAsync(WindowId)
            .Returns(new CheckingWindowDto { Id = WindowId, Title = "KS4 June", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June, StartDate = DateTime.UtcNow, EndDate = new(2026, 6, 26, 17, 0, 0) });

        var result = await _sut.DeleteAsync(WindowId, "REF001");

        Assert.False(result.WasHardDeleted);
        await _requestRepository.Received(1).WithdrawAsync(WindowId, 100000L, "REF001");
        await _requestRepository.DidNotReceive().DeleteAsync(WindowId, 100000L, "REF001");
        await _requestStateBlobClient.DidNotReceive().DeleteAsync(WindowId, "REF001");
    }

    private static AmendmentRequestData AmendmentRow(RequestStatus status, string first, string surname) =>
        new()
        {
            PupilFirstname = first,
            PupilSurname = surname,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove",
            Status = status,
            ReferenceNumber = "REF001"
        };

    [Fact]
    public async Task DeleteAsync_WhenAmendment_DelegatesAmendmentWithdrawnNotification()
    {
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, "REF001")
            .Returns(AmendmentRow(RequestStatus.SubmittedUnCommitted, "Jane", "Smith"));
        _checkYourPupilDataService.GetCheckingWindowAsync(WindowId)
            .Returns(new CheckingWindowDto { Id = WindowId, Title = "KS4 June", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June, StartDate = DateTime.UtcNow, EndDate = new(2026, 6, 26, 17, 0, 0) });

        await _sut.DeleteAsync(WindowId, "REF001");

        await _requestNotificationService.Received(1).NotifyAmendmentWithdrawnAsync("REF001", new(2026, 6, 26, 17, 0, 0));
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmCorrect_DelegatesDataCheckWithdrawnNotification()
    {
        var confirmCorrectRow = new AmendmentRequestData
        {
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            RequestType = RequestType.ConfirmCorrect,
            RequestTypeDescription = "Confirm Pupil Data Declaration",
            Status = RequestStatus.SubmittedUnCommitted,
            ReferenceNumber = "REF001"
        };
        _requestRepository.GetAmendmentRequestAsync(WindowId, 100000L, "REF001")
            .Returns(confirmCorrectRow);
        _checkYourPupilDataService.GetCheckingWindowAsync(WindowId)
            .Returns(new CheckingWindowDto { Id = WindowId, Title = "KS4 June", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June, StartDate = DateTime.UtcNow, EndDate = new(2026, 6, 26, 17, 0, 0) });

        await _sut.DeleteAsync(WindowId, "REF001");

        await _requestNotificationService.Received(1).NotifyDataCheckWithdrawnAsync("REF001", new(2026, 6, 26, 17, 0, 0));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<RequestDocument> CaptureDocument(RequestState journey, QuestionFlowConfig config)
    {
        SetupConfig(config);
        RequestDocument? captured = null;
        await _queueService.EnqueueAsync(Arg.Any<string>(), Arg.Do<RequestDocument>(d => captured = d));

        await _sut.ConfirmRequestAsync(WindowId, journey);

        return captured!;
    }

    private void SetupConfig(QuestionFlowConfig config) =>
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(config);

    private static RequestState ValidJourney(
        List<string>? history = null,
        Dictionary<string, QuestionAnswer>? answers = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = new CheckingWindowDto
            {
                Id = Guid.NewGuid(),
                Title = "KS4 June",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(20)
            },
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            QuestionHistory = history ?? [],
            QuestionAnswers = answers ?? new()
        };
        state.SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "Jane",
            Surname = "Smith",
            Sex = "F",
            DateOfBirth = "01/01/2010",
            Age = 16,
            Cypmd_Id = "CYPMD123",
            Upn = "123123"
        };
        return state;
    }

    private static (RequestState journey, QuestionFlowConfig config) MakeSubmission()
    {
        var config = MakeConfig([
            new JourneyPage { Id = "reason", Questions = [MakeQuestion(QuestionType.Radio)] }
        ]);
        return (ValidJourney(history: ["reason"]), config);
    }

    private static QuestionFlowConfig MakeConfig(List<JourneyPage> pages) =>
        new() { FirstPageId = pages[0].Id, Pages = pages };

    private static Question MakeQuestion(QuestionType type, string id = "q1") =>
        new() { Id = id, Type = type, Title = "My question" };
}
