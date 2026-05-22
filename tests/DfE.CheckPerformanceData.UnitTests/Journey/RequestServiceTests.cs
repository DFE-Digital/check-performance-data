using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class RequestServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IRequestBlobClient _blobClient = Substitute.For<IRequestBlobClient>();
    private readonly IDraftBlobClient _draftBlobClient = Substitute.For<IDraftBlobClient>();
    private readonly IRequestRepository _requestRepository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly RequestService _sut;

    public RequestServiceTests()
    {
        _currentUser.UserId.Returns("11111111-1111-1111-1111-111111111111");
        _currentUser.DisplayName.Returns("Test User");
        _currentUser.OrganisationUrn.Returns("100000");
        _currentUser.OrganisationName.Returns("Test School");
        _sut = new RequestService(_flowService, _blobClient, _draftBlobClient, _requestRepository, _currentUser);
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
    public async Task ConfirmRequestAsync_WhenAlreadySubmitted_ReturnsSilentlyWithoutWrites()
    {
        var journey = ValidJourney();
        _requestRepository.IsSubmittedAsync(journey.ReferenceNumber!).Returns(true);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _blobClient.DidNotReceive().SaveRequestAsync(Arg.Any<Guid>(), Arg.Any<RequestDocument>());
        await _requestRepository.DidNotReceive().UpsertAsync(Arg.Any<ChangeRequestData>());
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
    public async Task ConfirmRequestAsync_SavesChangeRequestToRepository()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _requestRepository.Received(1).UpsertAsync(Arg.Any<ChangeRequestData>());
    }

    [Fact]
    public async Task ConfirmRequestAsync_SavesRepositoryAfterBlob()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);
        var callOrder = new List<string>();
        _blobClient.SaveRequestAsync(Arg.Any<Guid>(), Arg.Any<RequestDocument>())
            .Returns(_ => { callOrder.Add("blob"); return Task.CompletedTask; });
        _requestRepository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(_ => { callOrder.Add("db"); return Task.CompletedTask; });

        await _sut.ConfirmRequestAsync(WindowId, journey);

        Assert.Equal(["blob", "db"], callOrder);
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
    public async Task ConfirmRequestAsync_PupilNameTemplate_IsResolved()
    {
        var question = new Question { Id = "q1", Type = QuestionType.FreeText, Title = "Notes for {pupilName}" };
        var config = MakeConfig([new JourneyPage { Id = "p1", Questions = [question] }]);
        var journey = ValidJourney(history: ["p1"]);

        var doc = await CaptureDocument(journey, config);

        Assert.Equal("Notes for Jane Smith", doc.Answers[0].QuestionTitle);
    }

    [Fact]
    public async Task ConfirmRequestAsync_SavesDocumentToBlobStorage()
    {
        var (journey, config) = MakeSubmission();
        SetupConfig(config);

        await _sut.ConfirmRequestAsync(WindowId, journey);

        await _blobClient.Received(1).SaveRequestAsync(WindowId, Arg.Any<RequestDocument>());
    }

    // ── SaveDraftAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraftAsync_WhenSessionIncomplete_Throws()
    {
        var journey = new RequestState(); // missing required fields

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SaveDraftAsync(WindowId, journey));
    }

    [Fact]
    public async Task SaveDraftAsync_WhenReferenceNumberMissing_Throws()
    {
        var journey = ValidJourney();
        journey.ReferenceNumber = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SaveDraftAsync(WindowId, journey));
    }

    [Fact]
    public async Task SaveDraftAsync_SavesBlobWithCorrectWindowIdAndReferenceNumber()
    {
        var journey = ValidJourney();

        await _sut.SaveDraftAsync(WindowId, journey);

        await _draftBlobClient.Received(1).SaveDraftAsync(WindowId, journey.ReferenceNumber!, journey);
    }

    [Fact]
    public async Task SaveDraftAsync_UpsertsRecordWithDraftStatusAndCorrectReferenceNumber()
    {
        var journey = ValidJourney();

        await _sut.SaveDraftAsync(WindowId, journey);

        await _requestRepository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.ReferenceNumber == journey.ReferenceNumber &&
            d.Status == RequestStatus.Draft));
    }

    [Fact]
    public async Task SaveDraftAsync_MapsCorrectPupilAndSchoolDetails()
    {
        var journey = ValidJourney();
        ChangeRequestData? captured = null;
        _requestRepository.UpsertAsync(Arg.Do<ChangeRequestData>(d => captured = d));

        await _sut.SaveDraftAsync(WindowId, journey);

        Assert.Equal("Jane", captured!.PupilFirstname);
        Assert.Equal("Smith", captured.PupilSurname);
        Assert.Equal("123123", captured.PupilUpn);
        Assert.Equal(100000L, captured.OrganisationUrn);
        Assert.Equal("Test User", captured.SubmittedByName);
        Assert.Equal(RequestStatus.Draft, captured.Status);
        // Config is null in this test so RequestType falls back to the WhatToChange prefix only
        Assert.Equal("Remove", captured.RequestType);
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

        await _sut.SaveDraftAsync(WindowId, journey);

        Assert.Equal("Remove - Permanently excluded", captured!.RequestType);
    }

    [Fact]
    public async Task SaveDraftAsync_SavesBlobBeforeUpsertingRecord()
    {
        var journey = ValidJourney();
        var callOrder = new List<string>();
        _draftBlobClient.SaveDraftAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<RequestState>())
            .Returns(_ => { callOrder.Add("blob"); return Task.CompletedTask; });
        _requestRepository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(_ => { callOrder.Add("db"); return Task.CompletedTask; });

        await _sut.SaveDraftAsync(WindowId, journey);

        Assert.Equal(["blob", "db"], callOrder);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<RequestDocument> CaptureDocument(RequestState journey, QuestionFlowConfig config)
    {
        SetupConfig(config);
        RequestDocument? captured = null;
        await _blobClient.SaveRequestAsync(WindowId, Arg.Do<RequestDocument>(d => captured = d));

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
