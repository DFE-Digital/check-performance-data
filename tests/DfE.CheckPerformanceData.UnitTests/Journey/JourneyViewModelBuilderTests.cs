using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyViewModelBuilderTests
{
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IOptionVisibilityService _optionVisibilityService = Substitute.For<IOptionVisibilityService>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly JourneyViewModelBuilder _sut;

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly JourneyPage PupilSearchPageConfig = new()
    {
        Id = "select-pupil", Type = PageType.PupilSearch,
        PupilKey = JourneyPage.PrimaryKey, PupilFilter = PupilFilter.Included, NextPageId = "q1"
    };

    private static readonly JourneyPage QuestionPage1 = new()
    {
        Id = "q1",
        Questions = [new Question { Id = "ans1", Type = QuestionType.Radio, Title = "Why?",
            Options = [new QuestionOption { Value = "reason-a", Label = "Reason A" }] }],
        NextPageId = "q2"
    };

    private static readonly JourneyPage QuestionPage2 = new()
    {
        Id = "q2",
        Questions = [new Question { Id = "ans2", Type = QuestionType.FreeText, Title = "Details?" }]
    };

    private static readonly JourneyPage ContentPage = new()
    {
        Id = "info", Type = PageType.Content
    };

    private static readonly QuestionFlowConfig Config = new()
    {
        FirstPageId = "select-pupil",
        Pages = [PupilSearchPageConfig, QuestionPage1, QuestionPage2, ContentPage]
    };

    private static readonly PupilDto Pupil = new()
    {
        Id = Guid.NewGuid(), Firstname = "Jane", Surname = "Smith",
        DateOfBirth = "27/07/2010", Sex = "F", Age = 16, Cypmd_Id = "CYPMD123", Upn = "UPN001"
    };

    public JourneyViewModelBuilderTests()
    {
        _journeyService.MaxEvidencePages.Returns(6);

        _flowService.GetPage(Config, "select-pupil").Returns(PupilSearchPageConfig);
        _flowService.GetPage(Config, "q1").Returns(QuestionPage1);
        _flowService.GetPage(Config, "q2").Returns(QuestionPage2);
        _flowService.GetPage(Config, "info").Returns(ContentPage);

        _optionVisibilityService
            .GetVisibleOptions(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(ci => ci.Arg<Question>().Options ?? (IReadOnlyList<QuestionOption>)[]);

        _sut = new JourneyViewModelBuilder(
            _flowService, _journeyService, _optionVisibilityService, _currentUserService);
    }

    // ── BuildSummaryVm ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSummaryVm_SkipsPupilSearchAndContentPages()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1", "info"]);
        journey.QuestionAnswers["ans1"] = new QuestionAnswer { TextValue = "reason-a" };

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.Single(vm.Rows);
        Assert.Equal("ans1", vm.Rows[0].Question.Id);
    }

    [Fact]
    public void BuildSummaryVm_SetsPupilNameFromSelectedPupil()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);
        journey.SelectedPupil = Pupil;

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.Equal("Jane Smith", vm.PupilName);
    }

    [Fact]
    public void BuildSummaryVm_BackPageIdIsLastHistoryEntry()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1", "q2"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.Equal("q2", vm.BackPageId);
    }

    [Fact]
    public void BuildSummaryVm_BackPageIsPupilSearchWhenLastHistoryEntryIsPupilSearchPage()
    {
        var journey = JourneyWithHistory(["select-pupil"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.True(vm.BackPageIsPupilSearch);
    }

    [Fact]
    public void BuildSummaryVm_WhenNoMatchedPupil_SecondRecordDisplayIsNull()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);
        journey.SelectedPupil = Pupil;

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.Null(vm.SecondRecordDisplay);
        Assert.Null(vm.FirstRecordDisplay);
    }

    [Fact]
    public void BuildSummaryVm_WhenMatchedPupil_PopulatesRecordDisplays()
    {
        var matchPupil = new PupilDto
        {
            Id = Guid.NewGuid(), Firstname = "John", Surname = "Doe",
            DateOfBirth = "02/02/2010", Sex = "M", Age = 16, Cypmd_Id = "CYPMD456", Upn = "UPN002"
        };
        var matchPage = new JourneyPage
        {
            Id = "select-match", Type = PageType.PupilSearch,
            PupilKey = JourneyPage.MatchKey, PupilFilter = PupilFilter.All
        };
        var mergeConfig = new QuestionFlowConfig
        {
            FirstPageId = "select-pupil",
            Pages = [PupilSearchPageConfig, matchPage, QuestionPage1]
        };
        _flowService.GetPage(mergeConfig, "select-pupil").Returns(PupilSearchPageConfig);
        _flowService.GetPage(mergeConfig, "select-match").Returns(matchPage);
        _flowService.GetPage(mergeConfig, "q1").Returns(QuestionPage1);

        var journey = JourneyWithHistory(["select-pupil", "select-match"]);
        journey.SelectedPupil = Pupil;
        journey.MatchedPupil = matchPupil;

        var vm = _sut.BuildSummaryVm(WindowId, journey, mergeConfig);

        Assert.Equal("Jane Smith, 27 July 2010", vm.FirstRecordDisplay);
        Assert.Equal("CYPMD456, John Doe", vm.SecondRecordDisplay);
    }

    [Fact]
    public void BuildSummaryVm_FromBulkFalseByDefault()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.False(vm.FromBulk);
    }

    [Fact]
    public void BuildSummaryVm_FromBulkPassedThrough()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config, fromBulk: true);

        Assert.True(vm.FromBulk);
    }

    [Fact]
    public void BuildSummaryVm_FromEditFalseByDefault()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.False(vm.FromEdit);
    }

    [Fact]
    public void BuildSummaryVm_FromEditPassedThrough()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config, fromEdit: true);

        Assert.True(vm.FromEdit);
    }

    [Fact]
    public void BuildSummaryVm_ConflictErrorPassedThrough()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config,
            conflictError: "A request already exists");

        Assert.Equal("A request already exists", vm.ConflictError);
    }

    [Fact]
    public void BuildSummaryVm_PrimaryPupilPageIdFromConfig()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildSummaryVm(WindowId, journey, Config);

        Assert.Equal("select-pupil", vm.PrimaryPupilPageId);
    }

    // ── BuildPageVm ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPageVm_WhenPageNotInHistory_BackPageIdIsLastHistoryEntry()
    {
        var journey = JourneyWithHistory(["select-pupil"]);

        var vm = _sut.BuildPageVm(WindowId, QuestionPage1, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary(), config: Config);

        Assert.Equal("select-pupil", vm.BackPageId);
    }

    [Fact]
    public void BuildPageVm_WhenPageIsFirstInHistory_BackPageIdIsNull()
    {
        var journey = JourneyWithHistory(["q1"]);

        var vm = _sut.BuildPageVm(WindowId, QuestionPage1, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary(), config: Config);

        Assert.Null(vm.BackPageId);
    }

    [Fact]
    public void BuildPageVm_WhenBackPageIsPupilSearch_BackPageIsPupilSearchIsTrue()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);

        var vm = _sut.BuildPageVm(WindowId, QuestionPage1, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary(), config: Config);

        Assert.Equal("select-pupil", vm.BackPageId);
        Assert.True(vm.BackPageIsPupilSearch);
    }

    [Fact]
    public void BuildPageVm_ModelStateErrorsPopulateQuestionModelErrors()
    {
        var journey = JourneyWithHistory(["select-pupil", "q1"]);
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("ans1", "This field is required");

        var vm = _sut.BuildPageVm(WindowId, QuestionPage1, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: modelState, config: Config);

        Assert.Equal("This field is required", vm.QuestionModels[0].Error);
    }

    [Fact]
    public void BuildPageVm_RadioQuestion_UsesOptionVisibilityService()
    {
        var visibleOpt = new QuestionOption { Value = "reason-a", Label = "Reason A" };
        _optionVisibilityService
            .GetVisibleOptions(QuestionPage1.Questions[0], Arg.Any<JourneyConditionContext>())
            .Returns([visibleOpt]);
        var journey = JourneyWithHistory(["select-pupil"]);

        var vm = _sut.BuildPageVm(WindowId, QuestionPage1, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary(), config: Config);

        Assert.Single(vm.QuestionModels[0].VisibleOptions);
        Assert.Equal("reason-a", vm.QuestionModels[0].VisibleOptions[0].Value);
    }

    [Fact]
    public void BuildPageVm_ResolvesQuestionTitleWithPupilName()
    {
        var pageWithTemplate = new JourneyPage
        {
            Id = "q-template",
            Questions = [new Question { Id = "qt", Type = QuestionType.FreeText,
                Title = "Why is {pupilName} leaving?" }]
        };
        _flowService.GetPage(Config, "q-template").Returns(pageWithTemplate);
        var journey = JourneyWithHistory(["select-pupil"]);
        journey.SelectedPupil = Pupil;

        var vm = _sut.BuildPageVm(WindowId, pageWithTemplate, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary(), config: Config);

        Assert.Equal("Why is Jane Smith leaving?", vm.QuestionModels[0].ResolvedTitle);
    }

    // ── BuildPupilSearchVm ───────────────────────────────────────────────────

    [Fact]
    public void BuildPupilSearchVm_ResolvesTitleWithPupilName()
    {
        var matchPage = new JourneyPage
        {
            Id = "select-match", Type = PageType.PupilSearch,
            PupilKey = JourneyPage.MatchKey, PupilFilter = PupilFilter.All,
            Title = "Who should {pupilName} be merged with?"
        };
        _flowService.GetPage(Config, "select-match").Returns(matchPage);
        var journey = JourneyWithHistory(["select-pupil"]);
        journey.SelectedPupil = Pupil;

        var vm = _sut.BuildPupilSearchVm(WindowId, "select-match", matchPage, journey, Config);

        Assert.Equal("Who should Jane Smith be merged with?", vm.Title);
    }

    [Fact]
    public void BuildPupilSearchVm_ForMatchKey_SetsExcludePupilIdFromSelectedPupil()
    {
        var primaryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var matchPage = new JourneyPage
        {
            Id = "select-match", Type = PageType.PupilSearch,
            PupilKey = JourneyPage.MatchKey, PupilFilter = PupilFilter.All
        };
        _flowService.GetPage(Config, "select-match").Returns(matchPage);
        var journey = JourneyWithHistory(["select-pupil"]);
        journey.SelectedPupilId = primaryId.ToString();

        var vm = _sut.BuildPupilSearchVm(WindowId, "select-match", matchPage, journey, Config);

        Assert.Equal(primaryId, vm.ExcludePupilId);
    }

    [Fact]
    public void BuildPupilSearchVm_ForPrimaryKey_ExcludePupilIdIsNull()
    {
        var journey = JourneyWithHistory([]);

        var vm = _sut.BuildPupilSearchVm(WindowId, "select-pupil", PupilSearchPageConfig, journey, Config);

        Assert.Null(vm.ExcludePupilId);
    }

    [Fact]
    public void BuildPupilSearchVm_WhenFirstVisitAndHistoryHasEntries_BackPageIdIsLastHistoryEntry()
    {
        var matchPage = new JourneyPage
        {
            Id = "select-match", Type = PageType.PupilSearch,
            PupilKey = JourneyPage.MatchKey, PupilFilter = PupilFilter.All
        };
        _flowService.GetPage(Config, "select-match").Returns(matchPage);
        var journey = JourneyWithHistory(["select-pupil"]);

        var vm = _sut.BuildPupilSearchVm(WindowId, "select-match", matchPage, journey, Config);

        Assert.Equal("select-pupil", vm.BackPageId);
        Assert.True(vm.BackPageIsPupilSearch);
    }

    [Fact]
    public void BuildPupilSearchVm_WhenFirstPageAndHistoryEmpty_BackPageIdIsNull()
    {
        var journey = JourneyWithHistory([]);

        var vm = _sut.BuildPupilSearchVm(WindowId, "select-pupil", PupilSearchPageConfig, journey, Config);

        Assert.Null(vm.BackPageId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RequestState JourneyWithHistory(List<string> history) => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new CheckingWindowDto
        {
            Id = Guid.NewGuid(), Title = "Test Window", KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(13)
        },
        QuestionHistory = history,
        QuestionAnswers = new Dictionary<string, QuestionAnswer>()
    };
}
