using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297310: submitting an Add-a-pupil request. Ticket B2 ("No rules engine outcomes") means an
// Add submission must persist a row and a journey blob exactly like every other amendment, but
// never enqueue to the rules engine — the LDS egress is a separate story. The "never enqueues"
// assertion is the guard on that boundary, mirroring RequestServiceResultsEnquiryTests.
public sealed class RequestServiceAddTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChangeRequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IQueueService _queue = Substitute.For<IQueueService>();
    private readonly IRequestNotificationService _notifications = Substitute.For<IRequestNotificationService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly RequestService _sut;

    public RequestServiceAddTests()
    {
        _currentUser.UserId.Returns("11111111-1111-1111-1111-111111111111");
        _currentUser.OrganisationUrn.Returns("142313");
        _currentUser.DisplayName.Returns("Ada Editor");
        _currentUser.Email.Returns("ada@school.test");
        _repository.UpsertAsync(Arg.Any<ChangeRequestData>()).Returns(ChangeRequestId);

        _sut = new RequestService(
            _flowService, _stateBlob, _repository, _currentUser,
            NullLogger<RequestService>.Instance, _queue, _notifications, _pupilData);
    }

    private static readonly QuestionFlowConfig AddConfig = new()
    {
        FirstPageId = "learner-details",
        Pages = [new JourneyPage { Id = "learner-details", Questions = [] }]
    };

    private static RequestState AddJourney() => new()
    {
        SelectedWhatToChange = WhatToChange.Add,
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
        SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Firstname = "Alice",
            Surname = "Newpupil",
            Sex = "F",
            DateOfBirth = "01/09/2010",
            Age = 0,
            Cypmd_Id = "",
            Identifier = "A123456789012"
        },
        QuestionHistory = ["learner-details"],
        QuestionAnswers = new()
    };

    private void SetupAddConfig() =>
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.KS4June).Returns(AddConfig);

    [Fact]
    public async Task SubmitRequestAsync_ForAdd_UpsertsAmendmentRow_WithAddAmendmentType()
    {
        SetupAddConfig();

        await _sut.SubmitRequestAsync(WindowId, AddJourney());

        await _repository.Received(1).UpsertAsync(Arg.Is<ChangeRequestData>(d =>
            d.RequestType == RequestType.Amendment &&
            d.AmendmentType == WhatToChange.Add &&
            d.RequestTypeDescription == "Add" &&
            d.PupilId == Guid.Parse("44444444-4444-4444-4444-444444444444") &&
            d.PupilFirstname == "Alice" &&
            d.PupilSurname == "Newpupil" &&
            d.PupilUpn == "A123456789012"));
    }

    [Fact]
    public async Task SubmitRequestAsync_ForAdd_NeverEnqueues()
    {
        SetupAddConfig();

        await _sut.SubmitRequestAsync(WindowId, AddJourney());

        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!);
    }

    [Fact]
    public async Task SubmitRequestAsync_ForAdd_SavesTheJourneyBlob()
    {
        SetupAddConfig();
        var journey = AddJourney();

        await _sut.SubmitRequestAsync(WindowId, journey);

        await _stateBlob.Received(1).SaveAsync(WindowId, "CYPMD_KS4June_ABC1234", journey);
    }

    [Fact]
    public async Task SubmitRequestAsync_ForRemove_StillEnqueues()
    {
        // Regression guard: the Add-only enqueue skip must not leak into other WhatToChange values.
        var removeConfig = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [new JourneyPage { Id = "reason", Questions = [] }]
        };
        _flowService.GetConfigAsync(WhatToChange.Remove, CheckingWindowType.KS4June).Returns(removeConfig);
        var journey = AddJourney();
        journey.SelectedWhatToChange = WhatToChange.Remove;
        journey.QuestionHistory = ["reason"];

        await _sut.SubmitRequestAsync(WindowId, journey);

        await _queue.Received(1).EnqueueAsync(QueueOptions.RulesEngineQueue, Arg.Any<RequestDocument>());
    }
}
