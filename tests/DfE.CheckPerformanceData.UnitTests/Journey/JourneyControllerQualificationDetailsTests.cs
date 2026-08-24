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
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297848: the QualificationDetails page — syllabus code, award date, missing grade, NCN. The
// grade scale for this journey ships inside the resolved qualification (no AODC/blob lookup), and
// the syllabus options are membership-validated against the same source.
public sealed class JourneyControllerQualificationDetailsTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F03");
    private const string CypmdId = "500001";

    private static readonly QualificationReference AqaMaths = new()
    {
        Qan = "60146084",
        QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
        AwardingOrganisation = "AQA",
        Grades = ["1", "2", "3"],
        SyllabusCodes =
        [
            new SyllabusCode { Code = "8300F", Title = "Mathematics Foundation Tier" },
            new SyllabusCode { Code = "8300H", Title = "Mathematics Higher Tier" }
        ]
    };

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService =
        new JourneyValidationService([new DfE.CheckPerformanceData.Application.Journey.Validators.NcnValidator()]);
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly JourneyViewModelBuilder _vmBuilder;
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IOptionVisibilityService _optionVisibility = Substitute.For<IOptionVisibilityService>();
    private readonly IQuestionOptionalityService _optionality = Substitute.For<IQuestionOptionalityService>();
    private readonly IOriginCountryLanguageCapture _originCapture = Substitute.For<IOriginCountryLanguageCapture>();
    private readonly IStudentResultsClient _results = Substitute.For<IStudentResultsClient>();
    private readonly IGradeReferenceClient _gradeReference = Substitute.For<IGradeReferenceClient>();
    private readonly IQualificationReferenceClient _qualificationReference = Substitute.For<IQualificationReferenceClient>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _notifications =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly JourneyController _sut;

    private static readonly JourneyPage DetailsPage = new()
    {
        Id = "qualification-details",
        Type = PageType.QualificationDetails,
        Title = "Provide the missing qualification details for {pupilName}",
        NextPageId = "additional-info",
        Questions =
        [
            new Question { Id = "q-syllabus-code", Type = QuestionType.SyllabusSelect, Title = "Select syllabus code",
                ValidationFailure = "Select the syllabus code" },
            new Question { Id = "q-award-date", Type = QuestionType.Date, Title = "Provide award date",
                ValidationFailure = "Provide the award date" },
            new Question { Id = "q-missing-grade", Type = QuestionType.GradeSelect, Title = "Select the missing grade {pupilName} achieved",
                ValidationFailure = "Select the missing grade this student achieved" },
            new Question { Id = "q-ncn", Type = QuestionType.FreeText, Title = "Provide National Centre Number (NCN) where exam was taken",
                Optional = true, Validator = "Ncn" }
        ]
    };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [DetailsPage]
    };

    public JourneyControllerQualificationDetailsTests()
    {
        _currentUser.OrganisationLaestab.Returns("860/4070");
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(Flow);
        _flowService.GetPage(Flow, "qualification-details").Returns(DetailsPage);
        _flowService.GetNextPageId(Flow, "qualification-details", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns("additional-info");
        _flowService.GetNavigationGuard(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>(), Arg.Any<string>())
            .Returns((JourneyNavigation?)null);
        _optionVisibility.GetVisibleOptions(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(ci => ci.Arg<Question>().Options ?? (IReadOnlyList<QuestionOption>)[]);
        _optionality.GetConditionallyOptionalQuestionIds(Arg.Any<JourneyPage>(), Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string>());

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _vmBuilder = new JourneyViewModelBuilder(_flowService, _journeyService, _optionVisibility, _currentUser);

        _sut = new JourneyController(
            _flowService, _journeyService, _fileStorage, _requestService, _pupilData, _vmBuilder,
            _analytics, _currentUser, _optionVisibility, _optionality, _originCapture, _results,
            _gradeReference, _qualificationReference, _notifications, OpenCheckingExercises.AlwaysOpen(),
            NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext },
            TempData = new TempDataDictionary(_httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    private static PupilDto Pupil() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
        DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = CypmdId, Identifier = "9900000001"
    };

    private RequestState ReadyJourney(Action<RequestState>? tweak = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.MissingQualification,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(),
            SelectedQualification = AqaMaths,
            QuestionHistory = ["select-student-single", "select-qualification", "qualification-details"]
        };
        tweak?.Invoke(state);
        _session.SetRequestState(WindowId, state);
        return state;
    }

    [Fact]
    public async Task The_details_page_renders_the_QualificationDetails_view_with_the_chosen_qualification()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "qualification-details"));

        Assert.Equal("QualificationDetails", view.ViewName);
        var model = Assert.IsType<PageViewModel>(view.Model);
        Assert.Equal("60146084", model.SelectedQualification!.Qan);
        Assert.Equal(CypmdId, model.CypmdId);
    }

    [Fact]
    public async Task The_grade_picker_offers_the_qualifications_own_scale_without_a_blob_call()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "qualification-details"));
        var model = Assert.IsType<PageViewModel>(view.Model);
        var gradeModel = model.QuestionModels.Single(q => q.Question.Id == "q-missing-grade");

        Assert.Equal(["1", "2", "3"], gradeModel.VisibleOptions.Select(o => o.Value).ToArray());
        await _gradeReference.DidNotReceiveWithAnyArgs().GetByQanAsync(default!, default);
    }

    [Fact]
    public async Task The_syllabus_options_pair_each_code_with_its_title()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.Page(WindowId, "qualification-details"));
        var model = Assert.IsType<PageViewModel>(view.Model);
        var syllabusModel = model.QuestionModels.Single(q => q.Question.Id == "q-syllabus-code");

        Assert.Equal(
            [("8300F", "8300F — Mathematics Foundation Tier"), ("8300H", "8300H — Mathematics Higher Tier")],
            syllabusModel.VisibleOptions.Select(o => (o.Value, o.Label)).ToArray());
    }

    [Fact]
    public async Task A_forged_syllabus_code_fails_closed_with_the_tickets_message()
    {
        ReadyJourney();
        var form = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["q_q_syllabus_code"] = "FORGED",
            ["q_q_award_date_day"] = "1", ["q_q_award_date_month"] = "6", ["q_q_award_date_year"] = "2025",
            ["q_q_missing_grade"] = "2"
        };
        _httpContext.Request.Form = new FormCollection(form);

        await _sut.PagePost(WindowId, "qualification-details", fromSummary: false);

        Assert.Equal("Select the syllabus code", _sut.ModelState["q-syllabus-code"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task The_same_grade_can_be_claimed_as_any_result_holds()
    {
        // There is no current grade on a missing qualification — the must-differ rule is
        // incorrect-grade's, and journey.SelectedResult is null here, so any offered grade passes.
        ReadyJourney();
        var form = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["q_q_syllabus_code"] = "8300H",
            ["q_q_award_date_day"] = "1", ["q_q_award_date_month"] = "6", ["q_q_award_date_year"] = "2025",
            ["q_q_missing_grade"] = "2"
        };
        _httpContext.Request.Form = new FormCollection(form);

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.PagePost(WindowId, "qualification-details", fromSummary: false));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("additional-info", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task An_ncn_over_five_characters_is_rejected_with_the_tickets_copy()
    {
        ReadyJourney();
        var form = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["q_q_syllabus_code"] = "8300H",
            ["q_q_award_date_day"] = "1", ["q_q_award_date_month"] = "6", ["q_q_award_date_year"] = "2025",
            ["q_q_missing_grade"] = "2",
            ["q_q_ncn"] = "123456"
        };
        _httpContext.Request.Form = new FormCollection(form);

        await _sut.PagePost(WindowId, "qualification-details", fromSummary: false);

        Assert.Equal(
            "National Centre Number (NCN) must be 5 characters or less",
            _sut.ModelState["q-ncn"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task A_blank_ncn_is_fine()
    {
        ReadyJourney();
        var form = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["q_q_syllabus_code"] = "8300H",
            ["q_q_award_date_day"] = "1", ["q_q_award_date_month"] = "6", ["q_q_award_date_year"] = "2025",
            ["q_q_missing_grade"] = "2",
            ["q_q_ncn"] = ""
        };
        _httpContext.Request.Form = new FormCollection(form);

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.PagePost(WindowId, "qualification-details", fromSummary: false));

        Assert.Equal("additional-info", redirect.RouteValues!["pageId"]);
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
