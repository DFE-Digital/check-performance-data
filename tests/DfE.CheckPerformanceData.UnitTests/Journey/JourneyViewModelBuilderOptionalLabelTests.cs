using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Issue #634 / AB#309468: the removal evidence page's two questions become optional when
// EalEvidenceIsOptional is true, but the page labels were built from the static Optional flag
// only. These tests wire the real optionality service and condition into the view-model builder,
// so the label and the validation that makes the page submittable cannot drift apart.
public class JourneyViewModelBuilderOptionalLabelTests
{
    private const string EvidenceTitle = "Upload files";
    private const string ExplanationTitle =
        "How does the evidence demonstrate why the pupil should be removed?";

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IOptionVisibilityService _optionVisibilityService = Substitute.For<IOptionVisibilityService>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly JourneyViewModelBuilder _sut;

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public JourneyViewModelBuilderOptionalLabelTests()
    {
        _journeyService.MaxEvidencePages.Returns(6);

        _optionVisibilityService
            .IsQuestionVisible(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(true);

        _flowService
            .BuildEvidenceContentBlocks(
                Arg.Any<JourneyPage>(),
                Arg.Any<Dictionary<string, QuestionAnswer>>(),
                Arg.Any<RequestState>(),
                Arg.Any<QuestionFlowConfig>())
            .Returns([]);

        _sut = new JourneyViewModelBuilder(
            _flowService,
            _journeyService,
            _optionVisibilityService,
            _currentUserService,
            new QuestionOptionalityService([new EalEvidenceIsOptionalCondition()]));
    }

    // AC 001: every one of the four waived answers produces the suffixed labels.
    [Theory]
    [InlineData("other")]
    [InlineData("believed-other")]
    [InlineData("chose-not-to-say")]
    [InlineData("not-known")]
    public void OptionalAnswers_MarkBothEvidenceLabelsOptional(string firstLanguage)
    {
        var (journey, page) = Setup("english-not-first-language", firstLanguage);

        var vm = Build(journey, page);

        Assert.Equal([$"{EvidenceTitle} (Optional)", $"{ExplanationTitle} (Optional)"],
            vm.QuestionModels.Select(q => q.ResolvedTitle).ToList());
    }

    // AC 003: changing the first-language answer re-evaluates the labels.
    [Fact]
    public void ChangingFirstLanguage_TogglesTheSuffix()
    {
        var (journey, page) = Setup("english-not-first-language", "english");
        Assert.Equal([EvidenceTitle, ExplanationTitle],
            Build(journey, page).QuestionModels.Select(q => q.ResolvedTitle).ToList());

        journey.QuestionAnswers["first-language"] = new QuestionAnswer { TextValue = "other" };

        Assert.Equal([$"{EvidenceTitle} (Optional)", $"{ExplanationTitle} (Optional)"],
            Build(journey, page).QuestionModels.Select(q => q.ResolvedTitle).ToList());
    }

    [Theory]
    [InlineData("english-not-first-language", "english")]
    [InlineData("english-not-first-language", "believed-english")]
    [InlineData("pupil-died", "other")]
    public void NonWaivedAnswers_LabelsStayMandatory(string reason, string firstLanguage)
    {
        var (journey, page) = Setup(reason, firstLanguage);

        var vm = Build(journey, page);

        Assert.Equal([EvidenceTitle, ExplanationTitle],
            vm.QuestionModels.Select(q => q.ResolvedTitle).ToList());
    }

    [Fact]
    public void RequireAtLeastOneActive_SuppressesSuffixEvenWhenConditionallyOptional()
    {
        var journey = Journey("english-not-first-language", "other");
        var page = EvidencePage(requireAtLeastOne: true);

        var vm = Build(journey, page);

        Assert.Equal([EvidenceTitle, ExplanationTitle],
            vm.QuestionModels.Select(q => q.ResolvedTitle).ToList());
    }

    private (RequestState Journey, JourneyPage Page) Setup(string? reason, string? firstLanguage) =>
        (Journey(reason, firstLanguage), EvidencePage());

    private PageViewModel Build(RequestState journey, JourneyPage page) =>
        _sut.BuildPageVm(WindowId, page, journey.QuestionAnswers, journey,
            fromSummary: false, modelState: new ModelStateDictionary());

    private static JourneyPage EvidencePage(bool requireAtLeastOne = false) => new()
    {
        Id = "evidence",
        Type = PageType.EvidenceUpload,
        Title = "Provide evidence for the removal of Jane Smith",
        RequireAtLeastOne = requireAtLeastOne,
        Questions =
        [
            new Question
            {
                Id = "evidence",
                Type = QuestionType.FileUpload,
                Title = EvidenceTitle,
                OptionalWhen = ["EalEvidenceIsOptional"]
            },
            new Question
            {
                Id = "how-evidence-supports",
                Type = QuestionType.TextArea,
                Title = ExplanationTitle,
                OptionalWhen = ["EalEvidenceIsOptional"]
            }
        ]
    };

    private static RequestState Journey(string? reason, string? firstLanguage)
    {
        var journey = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            QuestionHistory = ["evidence"],
            QuestionAnswers = new Dictionary<string, QuestionAnswer>()
        };

        if (reason is not null)
            journey.QuestionAnswers["reason"] = new QuestionAnswer { TextValue = reason };
        if (firstLanguage is not null)
            journey.QuestionAnswers["first-language"] = new QuestionAnswer { TextValue = firstLanguage };

        return journey;
    }
}
