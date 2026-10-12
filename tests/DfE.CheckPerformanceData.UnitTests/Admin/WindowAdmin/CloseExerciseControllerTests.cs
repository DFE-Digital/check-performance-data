using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#301022: Close ends an OPEN exercise before its scheduled end, once the admin has typed the
// window name, and then runs the hand-over sweep. The controller is thin: guard the request, check
// the typed name, call IExerciseEarlyClosureService, then ICloseExerciseService, report. The
// pre-close checks are pinned by ExerciseEarlyClosureServiceTests and the sweep by
// CloseExerciseServiceTests — nothing here re-tests either.
public class CloseExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;
    private const string WindowTitle = "KS4 June 2026";
    private static readonly DateTime ScheduledEnd = new(2026, 6, 30, 17, 0, 0);
    private static readonly DateTime ClosedAt = new(2026, 6, 10, 10, 39, 27);

    private readonly ICloseExerciseService _closeService = Substitute.For<ICloseExerciseService>();
    private readonly IExerciseEarlyClosureService _earlyClosure = Substitute.For<IExerciseEarlyClosureService>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    public CloseExerciseControllerTests()
    {
        _currentUser.UserId.Returns("sub-1");
        _currentUser.DisplayName.Returns("Banks Jamgbadi");
    }

    private static CheckingWindowDto Window(params CheckingExerciseType[] exercises) => new()
    {
        Id = WindowId,
        Title = WindowTitle,
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = ScheduledEnd,
        Exercises = exercises.Select((e, i) => new CheckingExerciseDto
        {
            ExerciseType = e,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = ScheduledEnd,
            SortOrder = i
        }).ToList()
    };

    private void TheWindowRuns(params CheckingExerciseType[] exercises) =>
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(exercises));

    private void TheCloseSucceeds()
    {
        _earlyClosure.CloseEarlyAsync(WindowId, Exercise, Arg.Any<EarlyClosureActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.Closed(ClosedAt, ScheduledEnd));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1 });
    }

    [Fact]
    public async Task Close_post_says_how_many_requests_are_waiting()
    {
        TheWindowRuns(Exercise);
        TheCloseSucceeds();
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1, Waiting = 1 });

        var controller = Build();
        await controller.Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        Assert.Equal(
            "Pupil data checking was closed early on 10/06/2026, 10:39 by Banks Jamgbadi. " +
            "3 requests sent for processing and 1 draft cancelled. " +
            "1 request is waiting for the Rules Engine. Send requests for processing again later.",
            controller.TempData[CloseExerciseController.TempDataKey]);
    }

    // Open unless a test says otherwise.
    private CloseExerciseController Build(ICheckingExerciseService? checkingExercises = null)
    {
        var controller = new CloseExerciseController(
            _closeService, _earlyClosure, _windowService,
            checkingExercises ?? OpenCheckingExercises.AlwaysOpen(), _currentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloseExerciseController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private async Task NothingWasClosedOrSwept()
    {
        await _earlyClosure.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default, default, default!, default);
        await _closeService.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default);
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_get_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns((CheckingWindowDto?)null);

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_returns_not_found_when_the_window_does_not_run_the_exercise()
    {
        // A hand-typed URL must not offer to close an exercise this window never had.
        TheWindowRuns(CheckingExerciseType.ResultsEnquiry);

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_refuses_an_exercise_that_is_not_open()
    {
        // AC2: an exercise that is already closed cannot be closed again — not even by URL.
        TheWindowRuns(Exercise);

        var controller = Build(OpenCheckingExercises.AlwaysClosed());
        var result = await controller.Confirm(WindowId, Exercise, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Pupil data checking is not open, so it was not closed.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
    }

    [Fact]
    public async Task Confirm_get_shows_the_window_the_exercise_and_its_scheduled_end()
    {
        // AC3. No preview counts: the ticket struck the "drafts flagged to the closing user" check.
        TheWindowRuns(Exercise);

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/Close.cshtml", view.ViewName);
        var vm = Assert.IsType<CloseExerciseViewModel>(view.Model);
        Assert.Equal(WindowId, vm.WindowId);
        Assert.Equal(WindowTitle, vm.WindowTitle);
        Assert.Equal(Exercise, vm.ExerciseType);
        Assert.Equal("Pupil data checking", vm.ExerciseLabel);
        Assert.Equal(ScheduledEnd, vm.ScheduledEnd);
        Assert.Null(vm.ConfirmWindowName);
        Assert.False(vm.HasError);
        await _closeService.DidNotReceiveWithAnyArgs().PreviewAsync(default, default, default);
    }

    // ── POST: refusals ───────────────────────────────────────────────────────

    [Fact]
    public async Task Close_post_does_not_close_an_exercise_the_window_does_not_run()
    {
        TheWindowRuns(CheckingExerciseType.ResultsEnquiry);

        var result = await Build().Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await NothingWasClosedOrSwept();
    }

    [Fact]
    public async Task Close_post_refuses_an_exercise_that_is_not_open_whatever_was_typed()
    {
        TheWindowRuns(Exercise);

        var controller = Build(OpenCheckingExercises.AlwaysClosed());
        var result = await controller.Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        await NothingWasClosedOrSwept();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Close_post_with_no_name_keeps_the_exercise_open_and_asks_for_the_name(string? typed)
    {
        TheWindowRuns(Exercise);

        var result = await Build().Close(WindowId, Exercise, typed, CancellationToken.None);

        var vm = Assert.IsType<CloseExerciseViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Enter the window name", vm.Error);
        await NothingWasClosedOrSwept();
    }

    [Theory]
    [InlineData("KS4 June")]            // short
    [InlineData("ks4 june 2026")]       // case matters: "exactly"
    [InlineData("KS4 June 2026 x")]     // long
    public async Task Close_post_with_the_wrong_name_keeps_the_exercise_open_and_says_it_does_not_match(string typed)
    {
        // AC4: the exercise stays open, the admin is told why, and can try again.
        TheWindowRuns(Exercise);

        var result = await Build().Close(WindowId, Exercise, typed, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/Close.cshtml", view.ViewName);
        var vm = Assert.IsType<CloseExerciseViewModel>(view.Model);
        Assert.Equal("The window name you entered does not match", vm.Error);
        Assert.True(vm.HasError);
        Assert.Equal(typed, vm.ConfirmWindowName);          // shown back so it can be corrected
        Assert.Equal(ScheduledEnd, vm.ScheduledEnd);
        await NothingWasClosedOrSwept();
    }

    // ── POST: the close ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("KS4 June 2026")]
    [InlineData("  KS4 June 2026  ")]   // white space either side is a paste artefact, not a different name
    public async Task Close_post_with_the_exact_name_closes_the_exercise_and_then_sweeps_it(string typed)
    {
        // AC5. The order is the point: the exercise is shut before its requests are handed over,
        // so no school can add a request behind the sweep.
        TheWindowRuns(Exercise);
        TheCloseSucceeds();

        var result = await Build().Close(WindowId, Exercise, typed, CancellationToken.None);

        Received.InOrder(() =>
        {
            _earlyClosure.CloseEarlyAsync(WindowId, Exercise, Arg.Any<EarlyClosureActor>(), Arg.Any<CancellationToken>());
            _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
        });
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
    }

    [Fact]
    public async Task Close_post_records_who_closed_it()
    {
        TheWindowRuns(Exercise);
        TheCloseSucceeds();

        await Build().Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        await _earlyClosure.Received(1).CloseEarlyAsync(
            WindowId, Exercise,
            Arg.Is<EarlyClosureActor>(a => a.UserId == "sub-1" && a.DisplayName == "Banks Jamgbadi"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Close_post_reports_when_who_and_what_the_sweep_did()
    {
        // The banner quotes the RESULTS: the close's own time, and what the sweep actually sent.
        TheWindowRuns(Exercise);
        TheCloseSucceeds();

        var controller = Build();
        await controller.Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        Assert.Equal(
            "Pupil data checking was closed early on 10/06/2026, 10:39 by Banks Jamgbadi. " +
            "3 requests sent for processing and 1 draft cancelled.",
            controller.TempData[CloseExerciseController.TempDataKey]);
        Assert.Null(controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
    }

    [Fact]
    public async Task Close_post_does_not_sweep_when_the_close_did_not_happen()
    {
        // The service found the exercise no longer open (a second admin, or the scheduled end
        // passing while the page was up). Nothing was closed by this press, so nothing is swept.
        TheWindowRuns(Exercise);
        _earlyClosure.CloseEarlyAsync(WindowId, Exercise, Arg.Any<EarlyClosureActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.NotOpen);

        var controller = Build();
        var result = await controller.Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(
            "Pupil data checking is not open, so it was not closed.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        Assert.Null(controller.TempData[CloseExerciseController.TempDataKey]);
        await _closeService.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default);
    }

    // ── POST: the sweep fails after the close has been saved ─────────────────

    [Fact]
    public async Task Close_post_says_the_exercise_closed_when_the_sweep_then_fails()
    {
        // The close and its audit row are already committed when the sweep runs, and the sweep
        // reads a blob and writes a queue row per request. If it throws, an error page would leave
        // the admin not knowing the exercise had closed. They are told it did, and where to retry.
        TheWindowRuns(Exercise);
        _earlyClosure.CloseEarlyAsync(WindowId, Exercise, Arg.Any<EarlyClosureActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.Closed(ClosedAt, ScheduledEnd));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloseExerciseResult>(new InvalidOperationException("queue unavailable")));

        var controller = Build();
        var result = await controller.Close(WindowId, Exercise, WindowTitle, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Pupil data checking was closed early on 10/06/2026, 10:39 by Banks Jamgbadi. " +
            "Its requests could not be sent for processing. " +
            "Select Send Pupil data checking requests for processing to try again.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        // Not the success banner: only half of what Close does has happened.
        Assert.Null(controller.TempData[CloseExerciseController.TempDataKey]);
    }

    [Fact]
    public async Task Close_post_does_not_swallow_a_cancelled_request()
    {
        TheWindowRuns(Exercise);
        _earlyClosure.CloseEarlyAsync(WindowId, Exercise, Arg.Any<EarlyClosureActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.Closed(ClosedAt, ScheduledEnd));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloseExerciseResult>(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Build().Close(WindowId, Exercise, WindowTitle, CancellationToken.None));
    }
}
