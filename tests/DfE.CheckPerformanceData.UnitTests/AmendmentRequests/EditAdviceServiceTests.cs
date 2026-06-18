using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class EditAdviceServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IRequestRepository _repo = Substitute.For<IRequestRepository>();
    private readonly IQuestionFlowService _flow = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _validation = Substitute.For<IJourneyValidationService>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();
    private readonly EditAdviceService _sut;

    public EditAdviceServiceTests()
    {
        _user.OrganisationUrn.Returns("123456");
        _flow.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns(new QuestionFlowConfig { FirstPageId = "select-pupil", Pages = [] });
        _sut = new EditAdviceService(_repo, _flow, _validation, _user);
    }

    private static RequestState Journey(WhatToChange type) => new()
    {
        ReferenceNumber = "REF001",
        SelectedWhatToChange = type,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId,
            Title = "W",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        }
    };

    private void StubStatus(RequestStatus status) =>
        _repo.GetAmendmentRequestAsync(WindowId, 123456, "REF001").Returns(new AmendmentRequestData
        {
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove",
            Status = status,
            ReferenceNumber = "REF001"
        });

    [Fact]
    public async Task Build_RemoveInProgress_RoutesToReasonPage()
    {
        StubStatus(RequestStatus.InProgress);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Equal(new ContinueToPage("reason"), advice!.ContinueTarget);
    }

    [Fact]
    public async Task Build_IncludeInProgress_RoutesToEvidencePage()
    {
        StubStatus(RequestStatus.InProgress);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Include));

        Assert.Equal(new ContinueToPage("evidence"), advice!.ContinueTarget);
    }

    [Fact]
    public async Task Build_MergeInProgress_RoutesToSummary()
    {
        StubStatus(RequestStatus.InProgress);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Merge));

        Assert.IsType<ContinueToSummary>(advice!.ContinueTarget);
    }

    [Fact]
    public async Task Build_ReadyToSubmit_AlwaysRoutesToSummary()
    {
        StubStatus(RequestStatus.ReadyToSubmit);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.IsType<ContinueToSummary>(advice!.ContinueTarget);
    }

    [Theory]
    [InlineData(WhatToChange.Remove, "This request is to remove a pupil. If this is not correct, go back and delete the request.")]
    [InlineData(WhatToChange.Merge, "This request is to merge pupil records. If this is not correct, go back and delete the request.")]
    [InlineData(WhatToChange.Include, "This request is to include a pupil. If this is not correct, go back and delete the request.")]
    public async Task Build_SetsAdviceTextForType(WhatToChange type, string expected)
    {
        StubStatus(RequestStatus.InProgress);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(type));

        Assert.Equal(expected, advice!.AdviceText);
    }

    [Fact]
    public async Task Build_InProgressWithInvalidEvidence_PopulatesMessages()
    {
        StubStatus(RequestStatus.InProgress);
        var evidencePage = new JourneyPage { Id = "evidence", Type = PageType.EvidenceUpload };
        _flow.GetReachableEvidencePage(Arg.Any<QuestionFlowConfig>(), Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns(evidencePage);
        _validation.ValidateEvidencePage(evidencePage, Arg.Any<RequestState>(), Arg.Any<string>())
            .Returns(new EvidenceValidationResult { Messages = ["Upload at least one file before continuing"] });

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Include));

        Assert.Contains("Upload at least one file before continuing", advice!.EvidenceMessages);
    }

    [Fact]
    public async Task Build_ReadyToSubmit_HasNoEvidenceMessages()
    {
        StubStatus(RequestStatus.ReadyToSubmit);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Include));

        Assert.Empty(advice!.EvidenceMessages);
    }

    [Fact]
    public async Task Build_WhenRequestNotFound_ReturnsNull()
    {
        _repo.GetAmendmentRequestAsync(WindowId, 123456, "REF001").Returns((AmendmentRequestData?)null);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Null(advice);
    }

    [Fact]
    public async Task Build_WhenConfigNotFound_ReturnsNull()
    {
        StubStatus(RequestStatus.InProgress);
        _flow.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns((QuestionFlowConfig?)null);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Null(advice);
    }

    [Fact]
    public async Task Build_InProgressWithNoReachableEvidencePage_HasNoEvidenceMessages()
    {
        StubStatus(RequestStatus.InProgress);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Empty(advice!.EvidenceMessages);
    }

    [Fact]
    public async Task Build_WhenJourneyIncomplete_ReturnsNull()
    {
        var advice = await _sut.BuildAsync(WindowId, "REF001", new RequestState());

        Assert.Null(advice);
    }

    [Fact]
    public async Task Build_Remove_SetsReasonForRemovalFromResolvedLabel()
    {
        StubStatus(RequestStatus.InProgress);
        _flow.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>())
            .Returns("Social care involvement - including police or prison");

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Equal("Social care involvement - including police or prison", advice!.ReasonForRemoval);
    }

    [Fact]
    public async Task Build_Remove_WhenReasonNotAnswered_ReasonForRemovalIsNull()
    {
        StubStatus(RequestStatus.InProgress);
        _flow.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>()).Returns(string.Empty);

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Remove));

        Assert.Null(advice!.ReasonForRemoval);
    }

    [Fact]
    public async Task Build_NonRemove_ReasonForRemovalIsNull()
    {
        StubStatus(RequestStatus.InProgress);
        _flow.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>())
            .Returns("should not be used");

        var advice = await _sut.BuildAsync(WindowId, "REF001", Journey(WhatToChange.Include));

        Assert.Null(advice!.ReasonForRemoval);
    }

    [Fact]
    public async Task Build_SetsPupilNameFromSelectedPupil()
    {
        StubStatus(RequestStatus.InProgress);
        var journey = Journey(WhatToChange.Remove);
        journey.SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "Jane",
            Surname = "Smith",
            Sex = "F",
            DateOfBirth = "2010-01-01",
            Age = 15,
            Cypmd_Id = "C1",
            Upn = "U1"
        };

        var advice = await _sut.BuildAsync(WindowId, "REF001", journey);

        Assert.Equal("Jane Smith", advice!.PupilName);
    }
}
