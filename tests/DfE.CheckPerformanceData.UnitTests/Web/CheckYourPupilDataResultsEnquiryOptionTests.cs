using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #317 (was AB#296648): the check-your-pupil-data page's "what would you like to do?" options are
// built from the exercises open right now, not from the window type. RequestChange and Confirm
// belong to PupilData and go together when it closes; ResultsEnquiry appears only while its own
// exercise is open — which is what lets a KS4 Autumn window offer an enquiry, where the old
// Post16-only test denied it.
public sealed class CheckYourPupilDataResultsEnquiryOptionTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime Yesterday = new(2026, 8, 19);
    private static readonly DateTime Tomorrow = new(2026, 8, 21);
    private static readonly DateTime LastMonth = new(2026, 7, 1);
    private static readonly DateTime NextMonth = new(2026, 9, 30);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly CheckYourPupilDataController _sut;

    public CheckYourPupilDataResultsEnquiryOptionTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _service.GetPupilTableAsync(WindowId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns((PupilTable.Empty, 0));

        // The real exercise and next-step services, on a fixed clock: the point of these tests is
        // which options the date rules produce, so substituting them would test nothing.
        var clock = new FixedTimeProvider(Now);
        var checkingExercises = new CheckingExerciseService(clock);

        _sut = new CheckYourPupilDataController(
            _service, _currentUser, _analytics,
            new NextStepsService(checkingExercises), checkingExercises)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static CheckingExerciseDto Exercise(
        CheckingExerciseType type, DateTime start, DateTime end, int sortOrder) =>
        new() { ExerciseType = type, StartDate = start, EndDate = end, SortOrder = sortOrder };

    private static CheckingExerciseDto Open(CheckingExerciseType type, int sortOrder = 0) =>
        Exercise(type, Yesterday, Tomorrow, sortOrder);

    private static CheckingExerciseDto Closed(CheckingExerciseType type, int sortOrder = 0) =>
        Exercise(type, LastMonth, Yesterday, sortOrder);

    private void Window(CheckingWindowType type, params CheckingExerciseDto[] exercises)
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Title = type == CheckingWindowType.Post16 ? "16 to 19 2026" : "KS4 2026",
            KeyStage = type == CheckingWindowType.Post16 ? KeyStages.Post16 : KeyStages.KS4,
            CheckingWindowType = type,
            // The outer window is the union of its exercises, and is deliberately still open in
            // every case below — the options must follow the exercises, never the outer dates.
            StartDate = LastMonth,
            EndDate = NextMonth,
            Exercises = [.. exercises]
        });
    }

    private async Task<CheckYourPupilDataViewModel> IndexModel()
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(WindowId));
        return Assert.IsType<CheckYourPupilDataViewModel>(view.Model);
    }

    private static CheckYourPupilDataViewModel Posted(NextSteps? step) => new()
    {
        WindowId = WindowId.ToString(),
        SelectedNextStep = step,
        WindowTitle = "", Sections = [], SectionsAsTabs = false,
        AvailableNextSteps = [], OrganisationName = "",
        LearnerNoun = LearnerNoun.Pupil,
        TitleContentKey = "check-pupil-data-title-ks4june"
    };

    // ── The options come from the open exercises ─────────────────────────────

    [Fact]
    public async Task Both_exercises_open_offers_all_three_options()
    {
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.Equal(
            [NextSteps.RequestChange, NextSteps.Confirm, NextSteps.ResultsEnquiry],
            (await IndexModel()).AvailableNextSteps);
    }

    [Fact]
    public async Task A_KS4_autumn_window_with_a_results_enquiry_exercise_offers_the_option()
    {
        // The old rule was a straight Post16 test, so KS4 Autumn could never offer an enquiry no
        // matter how it was configured.
        Window(CheckingWindowType.KS4Autumn,
            Open(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.Contains(NextSteps.ResultsEnquiry, (await IndexModel()).AvailableNextSteps);
    }

    [Fact]
    public async Task A_window_whose_only_exercise_is_pupil_data_is_unchanged()
    {
        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData, 0));

        Assert.Equal(
            [NextSteps.RequestChange, NextSteps.Confirm],
            (await IndexModel()).AvailableNextSteps);
    }

    [Fact]
    public async Task When_pupil_data_closes_amend_and_confirm_both_go()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.Equal([NextSteps.ResultsEnquiry], (await IndexModel()).AvailableNextSteps);
    }

    [Fact]
    public async Task With_no_open_exercise_no_option_is_offered()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Closed(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.Empty((await IndexModel()).AvailableNextSteps);
    }

    [Fact]
    public async Task A_window_with_no_exercises_offers_nothing_even_though_it_is_open()
    {
        // Fails closed. The outer window brackets now, but a half-configured window must not open a
        // journey by accident.
        Window(CheckingWindowType.Post16);

        Assert.Empty((await IndexModel()).AvailableNextSteps);
    }

    // ── The deadline sentence follows the pupil-data exercise ────────────────

    [Fact]
    public async Task The_deadline_is_the_pupil_data_exercises_end_date_not_the_windows()
    {
        // The outer window runs to NextMonth. Promising that date would give schools months of
        // slack they do not have.
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            Exercise(CheckingExerciseType.ResultsEnquiry, Yesterday, NextMonth, 1));

        var model = await IndexModel();

        Assert.Equal(Tomorrow, model.PupilDataEndDate);
        Assert.True(model.IsPupilDataOpen);
    }

    [Fact]
    public async Task After_pupil_data_closes_the_deadline_sentence_turns_past_tense()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        var model = await IndexModel();

        Assert.Equal(Yesterday, model.PupilDataEndDate);
        Assert.False(model.IsPupilDataOpen);
    }

    [Fact]
    public async Task A_window_with_no_pupil_data_exercise_has_no_deadline_to_show()
    {
        Window(CheckingWindowType.Post16, Open(CheckingExerciseType.ResultsEnquiry, 0));

        var model = await IndexModel();

        Assert.Null(model.PupilDataEndDate);
        Assert.False(model.IsPupilDataOpen);
    }

    // ── Routing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_offered_option_routes_where_it_did()
    {
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        var change = Assert.IsType<RedirectToActionResult>(await _sut.NextStep(WindowId, Posted(NextSteps.RequestChange)));
        Assert.Equal("WhatToChange", change.ControllerName);

        var confirm = Assert.IsType<RedirectToActionResult>(await _sut.NextStep(WindowId, Posted(NextSteps.Confirm)));
        Assert.Equal("ConfirmCorrect", confirm.ControllerName);

        var enquiry = Assert.IsType<RedirectToActionResult>(await _sut.NextStep(WindowId, Posted(NextSteps.ResultsEnquiry)));
        Assert.Equal("ResultIssue", enquiry.ControllerName);
        Assert.Equal(WindowId, enquiry.RouteValues!["windowId"]);
    }

    // ── The rule is enforced server-side, not just by not rendering a radio ──

    [Theory]
    [InlineData(NextSteps.ResultsEnquiry)]
    [InlineData(NextSteps.RequestChange)]
    [InlineData(NextSteps.Confirm)]
    public async Task Posting_an_option_whose_exercise_is_closed_is_rejected_as_unanswered(NextSteps step)
    {
        // Not rendering the option is a UI courtesy. A hand-crafted post must not start a journey
        // for an exercise that is shut.
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Closed(CheckingExerciseType.ResultsEnquiry, 1));

        var view = Assert.IsType<ViewResult>(await _sut.NextStep(WindowId, Posted(step)));

        Assert.Equal("Index", view.ViewName);
        Assert.Equal(
            "Select what you would like to do",
            _sut.ModelState[nameof(CheckYourPupilDataViewModel.SelectedNextStep)]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Posting_an_enquiry_on_a_window_with_no_enquiry_exercise_is_rejected()
    {
        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData, 0));

        var view = Assert.IsType<ViewResult>(await _sut.NextStep(WindowId, Posted(NextSteps.ResultsEnquiry)));

        Assert.Equal("Index", view.ViewName);
    }

    [Fact]
    public async Task A_rejected_post_does_not_record_the_choice_in_session()
    {
        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData, 0));

        await _sut.NextStep(WindowId, Posted(NextSteps.ResultsEnquiry));

        Assert.Null(_session.GetRequestState(WindowId).SelectedNextStep);
    }

    [Fact]
    public async Task A_rejected_post_reports_a_coded_validation_error()
    {
        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData, 0));

        await _sut.NextStep(WindowId, Posted(NextSteps.ResultsEnquiry));

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ValidationErrorEvent>(e =>
            e.ErrorCodes.Contains("no_selection")));
    }

    [Fact]
    public async Task Posting_nothing_is_still_rejected()
    {
        Window(CheckingWindowType.Post16, Open(CheckingExerciseType.PupilData, 0));

        var view = Assert.IsType<ViewResult>(await _sut.NextStep(WindowId, Posted(null)));

        Assert.Equal("Index", view.ViewName);
    }

    [Fact]
    public async Task An_accepted_post_records_the_choice_in_session()
    {
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        await _sut.NextStep(WindowId, Posted(NextSteps.ResultsEnquiry));

        Assert.Equal(NextSteps.ResultsEnquiry, _session.GetRequestState(WindowId).SelectedNextStep);
    }

    // ── The learner noun follows the window type ─────────────────────────────

    [Fact]
    public async Task A_16_to_19_window_calls_a_learner_a_student()
    {
        Window(CheckingWindowType.Post16, Open(CheckingExerciseType.PupilData));

        var model = await IndexModel();

        Assert.Equal("student", model.LearnerNoun.Singular);
        Assert.Equal("Check your student data", model.Title);
        // Both sections' wording is built from the same noun, so no table can disagree with the
        // heading above it.
        Assert.All(model.Sections, s => Assert.Equal("student", s.LearnerNoun.Singular));
        Assert.Contains(model.Sections, s => s.TabLabel == "Included students");
    }

    [Fact]
    public async Task Every_other_key_stage_calls_a_learner_a_pupil()
    {
        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData));

        var model = await IndexModel();

        Assert.Equal("pupil", model.LearnerNoun.Singular);
        Assert.Equal("Check your pupil data", model.Title);
        Assert.Contains(model.Sections, s => s.TabLabel == "Included pupils");
    }

    [Fact]
    public async Task The_pages_content_keys_are_scoped_to_the_window_type()
    {
        // A content block seeds once per key, so 16-19 needs its own or it would inherit the KS4
        // block's "pupil" wording — and an editor could never word the two differently.
        Window(CheckingWindowType.Post16, Open(CheckingExerciseType.PupilData));
        var post16 = await IndexModel();

        Window(CheckingWindowType.KS4June, Open(CheckingExerciseType.PupilData));
        var ks4 = await IndexModel();

        Assert.NotEqual(ks4.TitleContentKey, post16.TitleContentKey);
        Assert.Equal("check-pupil-data-title-post16", post16.TitleContentKey);
        Assert.All(post16.Sections, s => Assert.EndsWith("-post16", s.EmptyContentKey));
    }

    // ── AB#298317: the enquiry-only state ────────────────────────────────────

    [Fact]
    public async Task Enquiry_only_is_recognised_when_results_enquiry_is_the_sole_open_exercise()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        var model = await IndexModel();

        Assert.True(model.OffersEnquiryOnly);
        Assert.True(model.IsResultsEnquiryOpen);
        Assert.False(model.IsPupilDataOpen);
    }

    [Fact]
    public async Task Enquiry_only_is_not_the_case_while_pupil_data_is_still_open()
    {
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.False((await IndexModel()).OffersEnquiryOnly);
    }

    [Fact]
    public async Task The_next_opportunity_is_formatted_as_month_and_year()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Title = "16 to 19 2026",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = LastMonth,
            EndDate = NextMonth,
            NextOpportunity = new DateTime(2027, 10, 1),
            Exercises = [Closed(CheckingExerciseType.PupilData, 0), Open(CheckingExerciseType.ResultsEnquiry, 1)]
        });

        Assert.Equal("October 2027", (await IndexModel()).NextOpportunity);
    }

    [Fact]
    public async Task A_window_without_a_next_opportunity_leaves_it_null()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));

        Assert.Null((await IndexModel()).NextOpportunity);
    }

    [Fact]
    public async Task Sign_out_is_accepted_in_the_enquiry_only_state_and_goes_to_the_sign_out_link()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Open(CheckingExerciseType.ResultsEnquiry, 1));
        // No real-auth identity on the test principal, so SignOutLink resolves to the
        // impersonation clear path without needing IUrlHelper.

        var result = await _sut.NextStep(WindowId, Posted(NextSteps.SignOut));

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        // AB#298317: this page's own Referer requires authentication, so the redirect carries an
        // explicit returnUrl of "/" rather than the default Referer-based one (see SignOutLink).
        Assert.Equal("/dev/impersonate/clear?returnUrl=%2F", redirect.Url);
        Assert.Null(_session.GetRequestState(WindowId).SelectedNextStep);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData)]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    public async Task Sign_out_is_rejected_as_unanswered_when_the_question_was_never_asked(CheckingExerciseType alsoOpen)
    {
        // Both exercises open (the many-option radios) or only pupil data open: neither page asks
        // the Yes/No question, so a forged SignOut is treated exactly like no answer.
        Window(CheckingWindowType.Post16,
            Open(CheckingExerciseType.PupilData, 0),
            alsoOpen == CheckingExerciseType.ResultsEnquiry
                ? Open(CheckingExerciseType.ResultsEnquiry, 1)
                : Closed(CheckingExerciseType.ResultsEnquiry, 1));

        var result = await _sut.NextStep(WindowId, Posted(NextSteps.SignOut));

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.False(_sut.ModelState.IsValid);
        Assert.Null(_session.GetRequestState(WindowId).SelectedNextStep);
    }

    [Fact]
    public async Task Sign_out_is_rejected_when_nothing_is_open()
    {
        Window(CheckingWindowType.Post16,
            Closed(CheckingExerciseType.PupilData, 0),
            Closed(CheckingExerciseType.ResultsEnquiry, 1));

        var view = Assert.IsType<ViewResult>(await _sut.NextStep(WindowId, Posted(NextSteps.SignOut)));
        Assert.False(_sut.ModelState.IsValid);
    }

    [Fact]
    public void Offers_enquiry_only_is_null_safe_for_a_binder_created_instance()
    {
        // MVC's validation visitor reads every property on the POSTed model, whose required
        // collections are null — the same trap LearnerNoun documents on this class.
        var bound = new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(), WindowTitle = "", Sections = null!, SectionsAsTabs = false,
            AvailableNextSteps = null!, OrganisationName = "", TitleContentKey = "k"
        };

        Assert.False(bound.OffersEnquiryOnly);
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
