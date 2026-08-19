using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648 / AB#297130: the "Incorrect grade details" page. Uses the real view-model builder rather
// than a substitute, because the thing most worth pinning is that the grade options reaching the view
// come from the AODC reference data for the SELECTED result's QAN — not from the flow config, which
// cannot know which qualification the user picked.
public sealed class JourneyControllerResultDetailsTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string Laestab = "860/4070";
    private const string CypmdId = "500001";

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = new JourneyValidationService();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IOptionVisibilityService _optionVisibility = Substitute.For<IOptionVisibilityService>();
    private readonly IQuestionOptionalityService _optionality = Substitute.For<IQuestionOptionalityService>();
    private readonly IGradeReferenceClient _gradeReference = Substitute.For<IGradeReferenceClient>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _notifications =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly JourneyController _sut;

    private static readonly Question RevisedGrade = new()
    {
        Id = "q-revised-grade",
        Type = QuestionType.GradeSelect,
        Title = "What should the revised grade be?",
        ValidationFailure = "Select the revised grade"
    };

    private static readonly JourneyPage GradeDetails = new()
    {
        Id = "grade-details",
        Type = PageType.ResultDetails,
        Title = "Incorrect grade details",
        NextPageId = "additional-info",
        Questions = [RevisedGrade]
    };

    private static readonly JourneyPage AdditionalInfo = new() { Id = "additional-info" };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [GradeDetails, AdditionalInfo]
    };

    private static readonly GradeReference Btec = new()
    {
        Qan = "60370683",
        QualificationTitle = "Pearson BTEC L1/L2 Tech Award in Sport",
        AwardingOrganisation = "Pearson",
        PassGrades = ["*2", "P1", "P2", "M1", "M2", "D1", "D2"],
        FailGrades = ["F", "Q", "R", "U", "X"]
    };

    public JourneyControllerResultDetailsTests()
    {
        _currentUser.OrganisationLaestab.Returns(Laestab);
        _currentUser.OrganisationUrn.Returns("142313");
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(Flow);
        _flowService.GetPage(Flow, "grade-details").Returns(GradeDetails);
        _flowService.GetPage(Flow, "additional-info").Returns(AdditionalInfo);
        _flowService.GetNextPageId(Flow, "grade-details", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns("additional-info");
        _flowService.GetNavigationGuard(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>(), Arg.Any<string>())
            .Returns((JourneyNavigation?)null);
        _optionality.GetConditionallyOptionalQuestionIds(Arg.Any<JourneyPage>(), Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string>());
        _gradeReference.GetByQanAsync("60370683", Arg.Any<CancellationToken>()).Returns(Btec);

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        var builder = new JourneyViewModelBuilder(_flowService, _journeyService, _optionVisibility, _currentUser);

        _sut = new JourneyController(
            _flowService, _journeyService, Substitute.For<IFileStorageService>(),
            Substitute.For<IRequestService>(), Substitute.For<ICheckYourPupilDataService>(), builder,
            _analytics, _currentUser, _optionVisibility, _optionality,
            Substitute.For<IOriginCountryLanguageCapture>(),
            Substitute.For<IStudentResultsClient>(), _gradeReference, _notifications,
            NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext },
            TempData = new TempDataDictionary(_httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    private static StudentResultRecord Result(string qan = "60370683", string grade = "M1") => new()
    {
        CypmdId = CypmdId, Qan = qan, QualificationName = "Pearson BTEC L1/L2 Tech Award in Sport",
        SyllabusCode = "31525H", Session = "S2024", Grade = grade, SourceFile = ResultsFileTags.Post16Main
    };

    private void Ready(StudentResultRecord? result = null, Dictionary<string, QuestionAnswer>? answers = null)
        => _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.IncorrectGrade,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = new PupilDto
            {
                Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
                DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = CypmdId, Identifier = "9900000001"
            },
            SelectedResult = result ?? Result(),
            QuestionAnswers = answers ?? [],
            QuestionHistory = ["select-result", "grade-details"]
        });

    private void Post(string? grade)
    {
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["q_q_revised_grade"] = grade ?? string.Empty
        });
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_renders_the_result_details_view()
    {
        Ready();

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "grade-details"));

        Assert.Equal("ResultDetails", view.ViewName);
    }

    [Fact]
    public async Task Get_offers_the_qualifications_own_grades_pass_before_fail()
    {
        Ready();

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "grade-details"));
        var vm = Assert.IsType<PageViewModel>(view.Model);
        var options = vm.NonFileUploadModels.Single().VisibleOptions;

        Assert.Equal(
            ["*2", "P1", "P2", "M1", "M2", "D1", "D2", "F", "Q", "R", "U", "X"],
            options.Select(o => o.Value).ToArray());
        Assert.Equal(options.Select(o => o.Value), options.Select(o => o.Label));
    }

    [Fact]
    public async Task Get_looks_the_grades_up_by_the_selected_results_qan()
    {
        Ready(Result(qan: "10025480"));

        await _sut.Page(WindowId, "grade-details");

        await _gradeReference.Received(1).GetByQanAsync("10025480", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_passes_the_selected_result_to_the_view_for_the_summary()
    {
        Ready(Result(grade: "D1"));

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "grade-details"));
        var vm = Assert.IsType<PageViewModel>(view.Model);

        Assert.NotNull(vm.SelectedResult);
        Assert.Equal("D1", vm.SelectedResult.Grade);
        Assert.Equal("Billy B", vm.PupilName);
    }

    [Fact]
    public async Task Get_with_no_reference_data_offers_nothing_and_says_so()
    {
        // A gap between the results CSVs and the AODC export is a real state — the two come from
        // different teams. The page must explain it rather than showing an empty control.
        Ready(Result(qan: "99999999"));
        _gradeReference.GetByQanAsync("99999999", Arg.Any<CancellationToken>()).Returns((GradeReference?)null);

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "grade-details"));
        var qm = Assert.IsType<PageViewModel>(view.Model).NonFileUploadModels.Single();

        Assert.Empty(qm.VisibleOptions);
        Assert.True(qm.GradeOptionsUnavailable);
    }

    [Fact]
    public async Task A_page_without_a_grade_picker_never_reads_the_reference_data()
    {
        // Guards against every page in every journey paying for a blob read.
        Ready();

        await _sut.Page(WindowId, "additional-info");

        await _gradeReference.DidNotReceiveWithAnyArgs().GetByQanAsync(default!);
    }

    // ── POST ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_a_valid_different_grade_continues()
    {
        Ready();
        Post("D1");

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.PagePost(WindowId, "grade-details", false));

        Assert.Equal("additional-info", redirect.RouteValues!["pageId"]);
        Assert.Equal("D1", _session.GetRequestState(WindowId).QuestionAnswers["q-revised-grade"].TextValue);
    }

    [Fact]
    public async Task Post_nothing_redisplays_the_page_with_the_required_message()
    {
        Ready();
        Post(null);

        var view = Assert.IsType<ViewResult>(await _sut.PagePost(WindowId, "grade-details", false));

        Assert.Equal("ResultDetails", view.ViewName);
        Assert.Equal("Select the revised grade", _sut.ModelState["q-revised-grade"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_the_current_grade_redisplays_the_page_with_the_must_differ_message()
    {
        Ready(Result(grade: "M1"));
        Post("M1");

        var view = Assert.IsType<ViewResult>(await _sut.PagePost(WindowId, "grade-details", false));

        Assert.Equal("ResultDetails", view.ViewName);
        Assert.Equal(
            "The revised grade must be different from the current grade",
            _sut.ModelState["q-revised-grade"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_a_grade_the_qualification_does_not_offer_is_rejected()
    {
        Ready();
        Post("9");

        var view = Assert.IsType<ViewResult>(await _sut.PagePost(WindowId, "grade-details", false));

        Assert.Equal("ResultDetails", view.ViewName);
        Assert.Equal("Select the revised grade", _sut.ModelState["q-revised-grade"]!.Errors[0].ErrorMessage);
        Assert.DoesNotContain("q-revised-grade", _session.GetRequestState(WindowId).QuestionAnswers.Keys);
    }

    [Fact]
    public async Task Post_with_no_reference_data_cannot_succeed()
    {
        Ready(Result(qan: "99999999"));
        _gradeReference.GetByQanAsync("99999999", Arg.Any<CancellationToken>()).Returns((GradeReference?)null);
        Post("M1");

        Assert.IsType<ViewResult>(await _sut.PagePost(WindowId, "grade-details", false));
    }

    [Fact]
    public async Task A_rejected_post_still_renders_the_grade_options()
    {
        // Without the reference data on the redisplay path the picker would come back empty and the
        // user would be stuck with an error they cannot clear.
        Ready();
        Post("9");

        var view = Assert.IsType<ViewResult>(await _sut.PagePost(WindowId, "grade-details", false));
        var qm = Assert.IsType<PageViewModel>(view.Model).NonFileUploadModels.Single();

        Assert.Equal(12, qm.VisibleOptions.Count);
        Assert.False(qm.GradeOptionsUnavailable);
    }

    [Fact]
    public async Task A_rejected_post_reports_a_coded_validation_error()
    {
        Ready();
        Post(null);

        await _sut.PagePost(WindowId, "grade-details", false);

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ValidationErrorEvent>(e =>
            e.ErrorFields.Contains("q-revised-grade")));
    }

    [Fact]
    public async Task Prefix_sharing_grades_are_accepted_as_a_real_change()
    {
        // The IB Diploma case: 24F is a fail, 24D is a pass.
        var ib = new GradeReference
        {
            Qan = "50034157", QualificationTitle = "IBO Level 3 International Baccalaureate Diploma",
            PassGrades = ["24B", "24D"], FailGrades = ["24F", "U"]
        };
        _gradeReference.GetByQanAsync("50034157", Arg.Any<CancellationToken>()).Returns(ib);
        Ready(Result(qan: "50034157", grade: "24F"));
        Post("24D");

        Assert.IsType<RedirectToActionResult>(await _sut.PagePost(WindowId, "grade-details", false));
        Assert.Equal("24D", _session.GetRequestState(WindowId).QuestionAnswers["q-revised-grade"].TextValue);
    }

    // ── The summary's completeness guard ─────────────────────────────────────

    [Fact]
    public async Task The_summary_sends_an_enquiry_with_no_chosen_result_back_to_the_result_page()
    {
        // The result lives outside QuestionAnswers, so the flow engine's answer walk cannot see it is
        // missing — the summary would otherwise render blank rows and accept a submission.
        var flow = FlowWithSearchPages();
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        _flowService.GetNextPageId(flow, Arg.Any<string>(), Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        Ready();
        _session.SaveRequestState(WindowId, s => s.SelectedResult = null);

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal(nameof(JourneyController.ResultSearchPage), redirect.ActionName);
        Assert.Equal("select-result", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task The_summary_sends_an_enquiry_with_no_revised_grade_back_to_the_grade_page()
    {
        var flow = FlowWithSearchPages();
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        _flowService.GetNextPageId(flow, Arg.Any<string>(), Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        Ready();

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("grade-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task A_complete_enquiry_reaches_the_summary()
    {
        var flow = FlowWithSearchPages();
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        _flowService.GetNextPageId(flow, Arg.Any<string>(), Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        Ready(answers: new Dictionary<string, QuestionAnswer>
        {
            ["q-revised-grade"] = new() { TextValue = "D1" }
        });

        var view = Assert.IsType<ViewResult>(await _sut.Summary(WindowId));

        Assert.True(Assert.IsType<SummaryViewModel>(view.Model).IsResultsEnquiry);
    }

    [Fact]
    public async Task A_blank_revised_grade_counts_as_missing()
    {
        var flow = FlowWithSearchPages();
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        _flowService.GetNextPageId(flow, Arg.Any<string>(), Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        Ready(answers: new Dictionary<string, QuestionAnswer>
        {
            ["q-revised-grade"] = new() { TextValue = "   " }
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal("grade-details", redirect.RouteValues!["pageId"]);
    }

    private QuestionFlowConfig FlowWithSearchPages()
    {
        var selectResult = new JourneyPage { Id = "select-result", Type = PageType.ResultSearch };
        var flow = new QuestionFlowConfig
        {
            FirstPageId = "cohort-scope",
            Pages = [selectResult, GradeDetails, AdditionalInfo]
        };
        _flowService.GetPage(flow, "select-result").Returns(selectResult);
        _flowService.GetPage(flow, "grade-details").Returns(GradeDetails);
        _flowService.GetPage(flow, "additional-info").Returns(AdditionalInfo);
        return flow;
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        public void Set(string key, byte[] value) => _store[key] = value;
        public void Remove(string key) => _store.Remove(key);
        public void Clear() => _store.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
    }

    private sealed class TestSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }
}
