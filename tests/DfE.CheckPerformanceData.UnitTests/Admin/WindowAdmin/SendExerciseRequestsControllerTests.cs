using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#301022: the hand-over sweep on its own, for an exercise that has closed. Thin by design:
// guard the request, call ICloseExerciseService, report. The sweep itself is pinned by
// CloseExerciseServiceTests — nothing here re-tests it.
public class SendExerciseRequestsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    private readonly ICloseExerciseService _closeService = Substitute.For<ICloseExerciseService>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICheckingExerciseService _checkingExercises = Substitute.For<ICheckingExerciseService>();

    public SendExerciseRequestsControllerTests()
    {
        // The default for these tests: the exercise has closed.
        _checkingExercises.HasClosed(default!, default).ReturnsForAnyArgs(true);
    }

    private static CheckingWindowDto Window(params CheckingExerciseType[] exercises) => new()
    {
        Id = WindowId,
        Title = "KS4 June 2026",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30),
        Exercises = exercises.Select((e, i) => new CheckingExerciseDto
        {
            ExerciseType = e,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 30),
            SortOrder = i
        }).ToList()
    };

    private SendExerciseRequestsController Build()
    {
        var controller = new SendExerciseRequestsController(_closeService, _windowService, _checkingExercises)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

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
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_refuses_an_exercise_that_has_not_closed()
    {
        // While an exercise is open the sweep belongs to Close, which ends the exercise first.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _checkingExercises.HasClosed(default!, default).ReturnsForAnyArgs(false);

        var controller = Build();
        var result = await controller.Confirm(WindowId, Exercise, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Pupil data checking has not closed, so no requests were sent.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        await _closeService.DidNotReceiveWithAnyArgs().PreviewAsync(default, default, default);
    }

    [Fact]
    public async Task Confirm_get_shows_the_preview_counts()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.PreviewAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExercisePreview { RequestsToClose = 4, DraftsToCancel = 2 });

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/SendRequests.cshtml", view.ViewName);
        var vm = Assert.IsType<SendRequestsViewModel>(view.Model);
        Assert.Equal(4, vm.RequestsToSend);
        Assert.Equal(2, vm.DraftsToCancel);
        Assert.Equal("KS4 June 2026", vm.WindowTitle);
        Assert.Equal("Pupil data checking", vm.ExerciseLabel);
        Assert.Equal($"/admin/windows/{WindowId}/PupilData/send-requests", vm.PostUrl);
        Assert.Equal($"/admin/windows/summary/{WindowId}", vm.CancelLink);
    }

    [Fact]
    public async Task Confirm_get_still_renders_when_there_is_nothing_to_send()
    {
        // Sending for an already-swept exercise is harmless; the page says so rather than erroring.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.PreviewAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExercisePreview { RequestsToClose = 0, DraftsToCancel = 0 });

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        var vm = Assert.IsType<SendRequestsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.HasNothingToDo);
    }

    [Fact]
    public async Task Send_post_runs_the_sweep_and_reports_what_it_did()
    {
        // The banner quotes the result, never the preview — rows can change in between.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1 });

        var controller = Build();
        var result = await controller.Send(WindowId, Exercise, CancellationToken.None);

        await _closeService.Received(1).CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "3 requests sent for processing and 1 draft cancelled for Pupil data checking.",
            controller.TempData[CloseExerciseController.TempDataKey]);
    }

    [Fact]
    public async Task Send_post_does_not_sweep_an_exercise_that_has_not_closed()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _checkingExercises.HasClosed(default!, default).ReturnsForAnyArgs(false);

        var result = await Build().Send(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        await _closeService.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default);
    }

    [Fact]
    public async Task Send_post_does_not_sweep_an_exercise_the_window_does_not_run()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Send(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _closeService.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default);
    }
}
