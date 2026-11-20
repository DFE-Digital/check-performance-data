using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class ChangeTypeSelectionServiceTests
{
    private const CheckingWindowType WindowType = CheckingWindowType.KS4June;

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ChangeTypeSelectionService _sut;

    private static readonly CheckingWindowDto Window = new()
    {
        Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        Title = "W",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = WindowType,
        StartDate = new DateTime(2026, 9, 1),
        EndDate = new DateTime(2026, 12, 1)
    };

    private static readonly QuestionFlowConfig FlowWithPupilSearch = new()
    {
        FirstPageId = "first-page",
        Pages = [new JourneyPage { Id = "first-page", Type = PageType.PupilSearch, PupilKey = JourneyPage.PrimaryKey }]
    };

    private static readonly QuestionFlowConfig FlowWithoutPupilSearch = new()
    {
        FirstPageId = "first-page",
        Pages = [new JourneyPage { Id = "first-page", PupilFromAnswers = true }]
    };

    public ChangeTypeSelectionServiceTests()
    {
        _sut = new ChangeTypeSelectionService(_flowService, _analytics);
    }

    [Fact]
    public async Task A_window_type_with_one_change_type_selects_it()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);

        var choice = await _sut.GetChoiceAsync(WindowType);

        Assert.Equal([WhatToChange.Remove], choice.Available);
        Assert.Equal(WhatToChange.Remove, choice.OnlyChange);
    }

    [Fact]
    public async Task A_window_type_with_more_than_one_change_type_selects_none()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Merge, WhatToChange.Remove]);

        var choice = await _sut.GetChoiceAsync(WindowType);

        Assert.Equal([WhatToChange.Merge, WhatToChange.Remove], choice.Available);
        Assert.Null(choice.OnlyChange);
    }

    [Fact]
    public async Task A_window_type_with_no_change_type_is_an_error()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetChoiceAsync(WindowType));

        Assert.Contains(WindowType.ToString(), error.Message);
    }

    [Fact]
    public async Task Starting_a_change_the_window_does_not_offer_is_refused_and_changes_nothing()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        var journey = new RequestState { ReferenceNumber = "REF" };

        var start = await _sut.StartJourneyAsync(journey, Window, WhatToChange.Merge);

        Assert.Equal(JourneyStart.Refused, start);
        Assert.Null(journey.SelectedWhatToChange);
        Assert.Null(journey.CheckingWindow);
        Assert.Equal("REF", journey.ReferenceNumber);
        await _flowService.DidNotReceive().GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>());
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Starting_a_change_on_a_window_type_with_no_change_type_is_refused()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([]);

        var start = await _sut.StartJourneyAsync(new RequestState(), Window, WhatToChange.Remove);

        Assert.Equal(JourneyStart.Refused, start);
    }

    [Fact]
    public async Task Starting_an_offered_change_with_no_flow_config_records_it_and_keeps_the_previous_request_state()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        _flowService.GetConfigAsync(WhatToChange.Remove, WindowType).Returns((QuestionFlowConfig?)null);
        var journey = StaleJourney();

        var start = await _sut.StartJourneyAsync(journey, Window, WhatToChange.Remove);

        Assert.True(start.Recorded);
        Assert.Null(start.FirstPageId);
        Assert.Equal(WhatToChange.Remove, journey.SelectedWhatToChange);
        Assert.Same(Window, journey.CheckingWindow);
        Assert.Equal("REF", journey.ReferenceNumber);
        Assert.Single(journey.QuestionAnswers);
        Assert.Single(journey.QuestionHistory);
        await _analytics.Received(1).TrackAsync(
            Arg.Is<ChangeTypeSelectedEvent>(e => e.WhatToChange == "Remove" && e.CheckingWindowType == "KS4June"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Starting_an_offered_change_records_it_and_returns_the_first_page()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        _flowService.GetConfigAsync(WhatToChange.Remove, WindowType).Returns(FlowWithPupilSearch);
        var journey = new RequestState();

        var start = await _sut.StartJourneyAsync(journey, Window, WhatToChange.Remove);

        Assert.True(start.Recorded);
        Assert.Equal("first-page", start.FirstPageId);
        Assert.Equal(WhatToChange.Remove, journey.SelectedWhatToChange);
        Assert.Same(Window, journey.CheckingWindow);
    }

    [Fact]
    public async Task Starting_an_offered_change_sends_the_change_type_selected_event()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        _flowService.GetConfigAsync(WhatToChange.Remove, WindowType).Returns(FlowWithPupilSearch);

        await _sut.StartJourneyAsync(new RequestState(), Window, WhatToChange.Remove);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ChangeTypeSelectedEvent>(e => e.WhatToChange == "Remove" && e.CheckingWindowType == "KS4June"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_analytics_failure_does_not_stop_the_journey_starting()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        _flowService.GetConfigAsync(WhatToChange.Remove, WindowType).Returns(FlowWithPupilSearch);
        _analytics.TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("analytics down")));

        var start = await _sut.StartJourneyAsync(new RequestState(), Window, WhatToChange.Remove);

        Assert.Equal(new JourneyStart(true, "first-page"), start);
    }

    [Fact]
    public async Task A_flow_with_a_pupil_search_keeps_the_previous_request_state()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Remove]);
        _flowService.GetConfigAsync(WhatToChange.Remove, WindowType).Returns(FlowWithPupilSearch);
        var journey = StaleJourney();

        await _sut.StartJourneyAsync(journey, Window, WhatToChange.Remove);

        Assert.Equal("REF", journey.ReferenceNumber);
        Assert.Equal("stale-pupil", journey.SelectedPupilId);
        Assert.Single(journey.QuestionAnswers);
        Assert.Single(journey.QuestionHistory);
    }

    [Fact]
    public async Task A_flow_without_a_pupil_search_clears_the_previous_request_state()
    {
        _flowService.GetPupilDataChangesAsync(WindowType).Returns([WhatToChange.Add]);
        _flowService.GetConfigAsync(WhatToChange.Add, WindowType).Returns(FlowWithoutPupilSearch);
        var journey = StaleJourney();

        await _sut.StartJourneyAsync(journey, Window, WhatToChange.Add);

        Assert.Null(journey.ReferenceNumber);
        Assert.Null(journey.SelectedPupil);
        Assert.Null(journey.SelectedPupilId);
        Assert.Null(journey.SelectedPupilLabel);
        Assert.Null(journey.MatchedPupil);
        Assert.Null(journey.MatchedPupilId);
        Assert.Null(journey.MatchedPupilLabel);
        Assert.Null(journey.SelectedResult);
        Assert.Null(journey.SelectedResultQualification);
        Assert.Null(journey.SelectedQualification);
        Assert.Null(journey.OriginCountryCode);
        Assert.Null(journey.OriginCountryLanguages);
        Assert.Empty(journey.QuestionAnswers);
        Assert.Empty(journey.QuestionHistory);
        Assert.Equal(WhatToChange.Add, journey.SelectedWhatToChange);
        Assert.Same(Window, journey.CheckingWindow);
    }

    private static RequestState StaleJourney() => new()
    {
        ReferenceNumber = "REF",
        SelectedPupil = new PupilDto { Id = Guid.NewGuid(), Firstname = "A", Surname = "B", Sex = "F", DateOfBirth = "01/09/2010", Age = 0, Cypmd_Id = "", Identifier = "X" },
        SelectedPupilId = "stale-pupil",
        SelectedPupilLabel = "B, A",
        MatchedPupil = new PupilDto { Id = Guid.NewGuid(), Firstname = "C", Surname = "D", Sex = "M", DateOfBirth = "01/09/2010", Age = 0, Cypmd_Id = "", Identifier = "Y" },
        MatchedPupilId = "stale-match",
        MatchedPupilLabel = "D, C",
        SelectedResult = new ResultRecord(),
        SelectedResultQualification = new QualificationReference { Qan = "1", QualificationTitle = "T" },
        SelectedQualification = new QualificationReference { Qan = "2", QualificationTitle = "U" },
        OriginCountryCode = "FR",
        OriginCountryLanguages = ["French"],
        QuestionAnswers = new() { ["q"] = new QuestionAnswer { TextValue = "x" } },
        QuestionHistory = ["first-page"]
    };
}
