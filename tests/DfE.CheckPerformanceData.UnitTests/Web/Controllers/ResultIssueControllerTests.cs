using System.Reflection;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using WhatToChangeCheckingExerciseMap = DfE.CheckPerformanceData.Application.WindowManagement.WhatToChangeCheckingExerciseMap;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// AB#296648: the way in to the incorrect-grade enquiry.
//
// Two behaviours carry most of the risk here. First, the late-results guidance: the ticket is
// explicit that it "informs the user. It does not stop the user continuing" — CONFIRMED by the BA on
// 2026-08-17 in preference to the Figma frame that greys the option out. Second, starting an enquiry
// must wipe the previous one: "when I start another, then none of my previous answers are carried
// over".
public sealed class ResultIssueControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string Laestab = "860/4070";

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly ILateResultsAvailability _lateResults = Substitute.For<ILateResultsAvailability>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly ResultIssueController _sut;

    private static readonly CheckingWindowDto Post16Window = new()
    {
        Title = "16 to 19 2026",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1),
        EndDate = new DateTime(2027, 3, 31)
    };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [new JourneyPage { Id = "cohort-scope" }, new JourneyPage { Id = "check-late-results" }]
    };

    public ResultIssueControllerTests()
    {
        _currentUser.OrganisationLaestab.Returns(Laestab);
        _service.GetCheckingWindowAsync(WindowId).Returns(Post16Window);
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(Flow);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new ResultIssueController(_service, _flowService, _lateResults, _currentUser, _checkingExercises, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    private void SecondLateResultsAvailable(bool available) =>
        _lateResults.IsSecondLateResultsAvailableAsync(WindowId, Laestab, Arg.Any<CancellationToken>())
            .Returns(available);

    private static RedirectToActionResult AssertJourneyRedirect(IActionResult result, string pageId)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Journey", redirect.ControllerName);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
        Assert.Equal(pageId, redirect.RouteValues!["pageId"]);
        return redirect;
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_the_view_with_the_window()
    {
        var result = await _sut.Index(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ResultIssueViewModel>(view.Model);
        Assert.Equal(WindowId, model.WindowId);
        Assert.Null(model.IssueType);
    }

    [Fact]
    public async Task Get_does_not_preselect_an_option_even_when_a_journey_is_already_in_session()
    {
        // The confirmation page's "Report another issue with an exam result" link comes back here,
        // and the AC says nothing carries over — a preselected radio would be exactly that.
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.IncorrectGrade,
            QuestionAnswers = { ["q-cohort-scope"] = new QuestionAnswer { TextValue = "yes" } }
        });

        var view = Assert.IsType<ViewResult>(await _sut.Index(WindowId));

        Assert.Null(Assert.IsType<ResultIssueViewModel>(view.Model).IssueType);
    }

    // ── POST: validation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_without_a_selection_redisplays_the_page_with_the_pinned_error(string? issueType)
    {
        var result = await _sut.Confirm(WindowId, new ResultIssueViewModel { WindowId = WindowId, IssueType = issueType });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.Equal(
            "Select what issue with the results you need to report",
            _sut.ModelState[nameof(ResultIssueViewModel.IssueType)]!.Errors[0].ErrorMessage);
        await _flowService.DidNotReceiveWithAnyArgs().GetConfigAsync(default, default);
    }

    [Fact]
    public async Task Post_without_a_selection_does_not_touch_the_journey_state()
    {
        var existing = new RequestState { SelectedWhatToChange = WhatToChange.Merge };
        _session.SetRequestState(WindowId, existing);

        await _sut.Confirm(WindowId, new ResultIssueViewModel { WindowId = WindowId, IssueType = null });

        Assert.Equal(WhatToChange.Merge, _session.GetRequestState(WindowId).SelectedWhatToChange);
    }

    [Fact]
    public async Task Post_without_a_selection_reports_a_coded_validation_error()
    {
        await _sut.Confirm(WindowId, new ResultIssueViewModel { WindowId = WindowId, IssueType = null });

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ValidationErrorEvent>(e =>
            e.ErrorCount == 1 &&
            e.ErrorCodes.Contains("no_selection") &&
            e.ErrorFields.Contains(nameof(ResultIssueViewModel.IssueType))));
    }

    [Fact]
    public async Task Post_with_an_unrecognised_option_is_rejected_as_unanswered()
    {
        // "Result does not belong to pupil" is a sibling ticket with no journey. A posted value for
        // it (or a forged one) must not start a journey there is no flow for.
        var result = await _sut.Confirm(WindowId, new ResultIssueViewModel { WindowId = WindowId, IssueType = "result-does-not-belong" });

        Assert.IsType<ViewResult>(result);
        await _flowService.DidNotReceiveWithAnyArgs().GetConfigAsync(default, default);
    }

    [Fact]
    public async Task Post_missing_qualification_starts_that_journey_at_its_first_page()
    {
        // Mirrors the incorrect-grade happy path, minus the late-results branch: the interstitial
        // is about the second late results file, which cannot contain a qualification the data
        // does not hold at all.
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16)
            .Returns(new QuestionFlowConfig { FirstPageId = "cohort-scope", Pages = [] });

        var result = await _sut.Confirm(WindowId, new ResultIssueViewModel
            { WindowId = WindowId, IssueType = ResultIssueViewModel.MissingQualification });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Journey", redirect.ControllerName);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("cohort-scope", redirect.RouteValues!["pageId"]);

        var state = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.MissingQualification, state.SelectedWhatToChange);
        Assert.Empty(state.QuestionHistory);
        await _lateResults.DidNotReceiveWithAnyArgs().IsSecondLateResultsAvailableAsync(default, default!, default);
    }

    // ── POST: the late-results branch ────────────────────────────────────────

    [Fact]
    public async Task When_the_second_late_results_file_is_missing_the_user_is_told_to_check_it_first()
    {
        SecondLateResultsAvailable(false);

        var result = await _sut.Confirm(WindowId, ValidPost());

        AssertJourneyRedirect(result, "check-late-results");
    }

    [Fact]
    public async Task When_the_second_late_results_file_has_landed_the_guidance_is_skipped()
    {
        SecondLateResultsAvailable(true);

        var result = await _sut.Confirm(WindowId, ValidPost());

        AssertJourneyRedirect(result, "cohort-scope");
    }

    [Fact]
    public async Task The_first_page_after_the_guidance_comes_from_the_flow_config_not_a_hardcoded_id()
    {
        // If the flow's firstPageId is ever renamed, the controller must follow it rather than
        // redirecting to a page that no longer exists.
        SecondLateResultsAvailable(true);
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16)
            .Returns(new QuestionFlowConfig { FirstPageId = "renamed-first-page", Pages = [] });

        var result = await _sut.Confirm(WindowId, ValidPost());

        AssertJourneyRedirect(result, "renamed-first-page");
    }

    [Fact]
    public async Task The_guidance_page_is_seeded_into_the_history_so_the_engine_lets_the_user_see_it()
    {
        // The flow's firstPageId is cohort-scope, so without seeding, the journey engine's
        // out-of-sequence guard bounces the user straight past the guidance — and the AC "then I am
        // told to check that file first" silently never happens. This was a real defect caught by
        // walking the journey; it is not reproducible from the redirect assertion alone.
        SecondLateResultsAvailable(false);

        await _sut.Confirm(WindowId, ValidPost());

        Assert.Equal(["check-late-results"], _session.GetRequestState(WindowId).QuestionHistory);
    }

    [Fact]
    public async Task No_page_is_seeded_when_the_guidance_is_skipped()
    {
        // cohort-scope IS the flow's firstPageId, so an empty history is what the guard expects.
        SecondLateResultsAvailable(true);

        await _sut.Confirm(WindowId, ValidPost());

        Assert.Empty(_session.GetRequestState(WindowId).QuestionHistory);
    }

    [Theory]
    [InlineData(false, true)]  // no LR2 -> guidance shown
    [InlineData(true, false)]  // LR2 present -> guidance skipped
    public async Task Starting_an_enquiry_records_whether_the_guidance_was_shown(
        bool lateResultsAvailable, bool expectedGuidanceShown)
    {
        // This flag is how we find out whether the interstitial is doing its job — stopping enquiries
        // that the November file would have fixed anyway.
        SecondLateResultsAvailable(lateResultsAvailable);

        await _sut.Confirm(WindowId, ValidPost());

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ResultsEnquiryStartedEvent>(e =>
            e.EnquiryType == "incorrect-grade" &&
            e.CheckingWindowType == "Post16" &&
            e.LateResultsGuidanceShown == expectedGuidanceShown));
    }

    [Fact]
    public async Task A_rejected_selection_does_not_record_a_started_enquiry()
    {
        // Nothing was started, so counting it would inflate the funnel's top.
        await _sut.Confirm(WindowId, new ResultIssueViewModel { WindowId = WindowId, IssueType = null });

        await _analytics.DidNotReceive().TrackSafeAsync(Arg.Any<ResultsEnquiryStartedEvent>());
    }

    [Fact]
    public async Task An_analytics_failure_does_not_stop_the_enquiry_starting()
    {
        // TrackSafeAsync is meant to swallow, but the journey must not depend on that.
        SecondLateResultsAvailable(true);
        _analytics.TrackSafeAsync(Arg.Any<ResultsEnquiryStartedEvent>())
            .Returns(_ => throw new InvalidOperationException("BigQuery is down"));

        var result = await _sut.Confirm(WindowId, ValidPost());

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Availability_is_asked_for_the_signed_in_school_and_this_window()
    {
        SecondLateResultsAvailable(true);

        await _sut.Confirm(WindowId, ValidPost());

        await _lateResults.Received(1)
            .IsSecondLateResultsAvailableAsync(WindowId, Laestab, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_option_is_always_selectable_regardless_of_late_results()
    {
        // The BA's decision: warn, never block. Both states must reach the journey — only the entry
        // page differs. A regression to blocking would show up as a ViewResult here.
        foreach (var available in new[] { true, false })
        {
            SecondLateResultsAvailable(available);

            var result = await _sut.Confirm(WindowId, ValidPost());

            Assert.IsType<RedirectToActionResult>(result);
        }
    }

    [Fact]
    public async Task A_missing_flow_config_falls_back_to_the_pupil_data_page_rather_than_a_dead_link()
    {
        SecondLateResultsAvailable(true);
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16)
            .Returns((QuestionFlowConfig?)null);

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Confirm(WindowId, ValidPost()));

        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    // ── POST: state reset ────────────────────────────────────────────────────

    [Fact]
    public async Task Starting_an_enquiry_clears_every_answer_from_the_previous_one()
    {
        // AC: "Given I have submitted an enquiry, when I start another, then none of my previous
        // answers are carried over."
        SecondLateResultsAvailable(true);
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupilLabel = "Smith, Jane",
            SelectedResult = new StudentResultRecord { Qan = "60181576", Grade = "6" },
            ReferenceNumber = "CYPMD_16to19_RE_ABCDEF1",
            OriginCountryCode = "FR",
            QuestionAnswers = { ["q-revised-grade"] = new QuestionAnswer { TextValue = "5" } },
            QuestionHistory = { "select-result", "grade-details" }
        });

        await _sut.Confirm(WindowId, ValidPost());

        var state = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.IncorrectGrade, state.SelectedWhatToChange);
        Assert.Null(state.SelectedPupilId);
        Assert.DoesNotContain("grade-details", state.QuestionHistory);
        Assert.Null(state.SelectedPupilLabel);
        Assert.Null(state.SelectedPupil);
        Assert.Null(state.SelectedResult);
        Assert.Null(state.ReferenceNumber);
        Assert.Null(state.OriginCountryCode);
        Assert.Empty(state.QuestionAnswers);
    }

    [Fact]
    public async Task Starting_an_enquiry_stamps_the_journey_with_the_window()
    {
        SecondLateResultsAvailable(true);

        await _sut.Confirm(WindowId, ValidPost());

        var state = _session.GetRequestState(WindowId);
        Assert.NotNull(state.CheckingWindow);
        Assert.Equal(CheckingWindowType.Post16, state.CheckingWindow.CheckingWindowType);
    }

    [Fact]
    public async Task Starting_an_enquiry_leaves_another_windows_journey_alone()
    {
        // Journey state is per-window; resetting this window must not wipe a half-finished KS4 one.
        SecondLateResultsAvailable(true);
        var otherWindow = Guid.NewGuid();
        _session.SetRequestState(otherWindow, new RequestState { SelectedWhatToChange = WhatToChange.Merge });

        await _sut.Confirm(WindowId, ValidPost());

        Assert.Equal(WhatToChange.Merge, _session.GetRequestState(otherWindow).SelectedWhatToChange);
    }

    [Fact]
    public async Task Starting_an_enquiry_clears_the_bulk_and_single_edit_modes()
    {
        // A fresh enquiry is never an edit of an existing request; leaving the flag set would make
        // the summary link back to the amendment-requests page.
        SecondLateResultsAvailable(true);
        _session.SetBulkEditMode(WindowId);
        _session.SetSingleEditMode(WindowId);

        await _sut.Confirm(WindowId, ValidPost());

        Assert.False(_session.IsBulkEditMode(WindowId));
        Assert.False(_session.IsSingleEditMode(WindowId));
    }

    // ── Attribute pins ───────────────────────────────────────────────────────

    [Fact]
    public void The_get_route_is_scoped_to_a_window()
    {
        var route = typeof(ResultIssueController)
            .GetMethod(nameof(ResultIssueController.Index))!
            .GetCustomAttribute<RouteAttribute>();

        Assert.Equal("/{windowId:guid}/ResultIssue", route?.Template);
    }

    [Fact]
    public void The_post_is_antiforgery_protected_and_shares_the_route()
    {
        var method = typeof(ResultIssueController).GetMethod(nameof(ResultIssueController.Confirm))!;

        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal("/{windowId:guid}/ResultIssue", method.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void The_controller_does_not_opt_out_of_authorisation()
    {
        // A school's results are not public. The global fallback policy authorises; an
        // [AllowAnonymous] anywhere here would silently opt this page out of it.
        Assert.Null(typeof(ResultIssueController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(
            typeof(ResultIssueController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    // ── AB#298229: cancel from the summary ───────────────────────────────────

    [Fact]
    public void Cancel_discards_everything_entered_for_the_current_enquiry()
    {
        // AC: "when I select 'Cancel and go back to create a new enquiry', then nothing I entered
        // is submitted" and "no data carried over". Before AB#298229 the summary link went to
        // Index, which clears nothing — the abandoned enquiry stayed resumable (and submittable)
        // via a deep link back to /Journey/{windowId}/summary.
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.MissingQualification,
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupilLabel = "Smith, Alice",
            QuestionAnswers = { ["q_award_date"] = new QuestionAnswer { TextValue = "2025-06-01" } },
            QuestionHistory = { "cohort-scope", "select-student-single" }
        });

        _sut.Cancel(WindowId);

        var state = _session.GetRequestState(WindowId);
        Assert.Null(state.SelectedWhatToChange);
        Assert.Null(state.SelectedPupilId);
        Assert.Null(state.SelectedPupilLabel);
        Assert.Empty(state.QuestionAnswers);
        Assert.Empty(state.QuestionHistory);
    }

    [Fact]
    public void Cancel_clears_the_bulk_and_single_edit_modes()
    {
        // Same hygiene as Confirm: a stale edit flag would make a later summary link back to the
        // amendment-requests page instead of the journey. Cleared only alongside the journey wipe —
        // a no-op cancel (nothing in progress) must leave an amendment's edit session untouched.
        _session.SetRequestState(WindowId, new RequestState { SelectedWhatToChange = WhatToChange.IncorrectGrade });
        _session.SetBulkEditMode(WindowId);
        _session.SetSingleEditMode(WindowId);

        _sut.Cancel(WindowId);

        Assert.False(_session.IsBulkEditMode(WindowId));
        Assert.False(_session.IsSingleEditMode(WindowId));
    }

    [Fact]
    public void Cancel_after_submission_keeps_the_confirmation_renderable()
    {
        // Review finding 1 (verified live, CYPMD_16to19_RE_98DF50C): session state is keyed
        // request_{windowId} alone, so every tab on a window shares one journey. Submitting leaves
        // only { CheckingWindow, ReferenceNumber } so EnquiryConfirmation can render; the cancel
        // link in a stale second tab must not wipe that residue, or the first tab's confirmation
        // page — the only on-screen copy of the reference — dies on refresh.
        _session.SetRequestState(WindowId, new RequestState
        {
            CheckingWindow = Post16Window,
            ReferenceNumber = "CYPMD_16to19_RE_98DF50C"
        });

        _sut.Cancel(WindowId);

        var state = _session.GetRequestState(WindowId);
        Assert.Equal("CYPMD_16to19_RE_98DF50C", state.ReferenceNumber);
        Assert.NotNull(state.CheckingWindow);
    }

    [Fact]
    public void Cancel_leaves_an_in_progress_amendment_journey_alone()
    {
        // The route is reachable by URL alone; a forged or emailed link must not destroy a
        // half-finished pupil-data amendment that happens to share the window's session slot.
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            QuestionAnswers = { ["q-removal-date"] = new QuestionAnswer { TextValue = "2026-07-01" } }
        });

        _sut.Cancel(WindowId);

        var state = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Remove, state.SelectedWhatToChange);
        Assert.NotEmpty(state.QuestionAnswers);
    }

    [Fact]
    public void Every_enquiry_member_cancels_and_no_amendment_member_does()
    {
        // Guards that name one enum member are the recurring bug class on this subsystem (the
        // AB#297848 Back link; the close-window replay). Resolving through the exercise map means
        // a third enquiry journey becomes cancellable the moment it is mapped — and this sweep
        // fails loudly if the guard ever regresses to naming members.
        foreach (var change in Enum.GetValues<WhatToChange>())
        {
            _session.SetRequestState(WindowId, new RequestState { SelectedWhatToChange = change });

            _sut.Cancel(WindowId);

            var cleared = _session.GetRequestState(WindowId).SelectedWhatToChange is null;
            var isEnquiry = WhatToChangeCheckingExerciseMap.CheckingExerciseFor(change)
                == CheckingExerciseType.ResultsEnquiry;
            Assert.Equal(isEnquiry, cleared);
        }
    }

    [Fact]
    public void Cancel_lands_on_the_issue_chooser()
    {
        // AC: "I am at the start of the enquiry journey" — the enquiry-type selection.
        var result = _sut.Cancel(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
    }

    [Fact]
    public void Cancel_leaves_another_windows_journey_alone()
    {
        // Journey state is per-window; cancelling this enquiry must not wipe a half-finished
        // KS4 amendment in another window.
        var otherWindow = Guid.NewGuid();
        _session.SetRequestState(otherWindow, new RequestState { SelectedWhatToChange = WhatToChange.Merge });

        _sut.Cancel(WindowId);

        Assert.Equal(WhatToChange.Merge, _session.GetRequestState(otherWindow).SelectedWhatToChange);
    }

    [Fact]
    public void Cancel_needs_no_window_lookup_so_it_works_after_the_exercise_closes()
    {
        // Abandoning must always work — including from a tab left open across the exercise's
        // closing date — and must record nothing. Index owns the closed-exercise redirect for
        // whatever the user does next; Cancel itself touches only session.
        _sut.Cancel(WindowId);

        Assert.Empty(_service.ReceivedCalls());
        Assert.Empty(_analytics.ReceivedCalls());
    }

    private static ResultIssueViewModel ValidPost() =>
        new() { WindowId = WindowId, IssueType = ResultIssueViewModel.IncorrectGrade };

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

    // ── #318: closed results-enquiry checking exercise ───────────────────────

    [Fact]
    public async Task Get_when_the_results_enquiry_exercise_has_closed_redirects_with_a_message()
    {
        _checkingExercises.Close();

        var result = await _sut.Index(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal(
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.ResultsEnquiry),
            _sut.TempData[ClosedExerciseGuard.TempDataKey]);
    }

    [Fact]
    public async Task Post_when_the_results_enquiry_exercise_has_closed_starts_no_enquiry()
    {
        _checkingExercises.Close();

        var result = await _sut.Confirm(WindowId,
            new ResultIssueViewModel { WindowId = WindowId, IssueType = ResultIssueViewModel.IncorrectGrade });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(_session.GetRequestState(WindowId).SelectedWhatToChange);
        await _analytics.DidNotReceiveWithAnyArgs().TrackSafeAsync(Arg.Any<ResultsEnquiryStartedEvent>());
    }

    [Fact]
    public async Task Post_gates_on_results_enquiry_not_on_pupil_data()
    {
        // Pupil data checking closes months before results enquiry on a 16-19 window; this entry
        // point must follow its own exercise, not the other one and not the outer window.
        _checkingExercises.IsOpen(default!, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == CheckingExerciseType.PupilData);

        var result = await _sut.Confirm(WindowId,
            new ResultIssueViewModel { WindowId = WindowId, IssueType = ResultIssueViewModel.IncorrectGrade });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(_session.GetRequestState(WindowId).SelectedWhatToChange);
    }

}
