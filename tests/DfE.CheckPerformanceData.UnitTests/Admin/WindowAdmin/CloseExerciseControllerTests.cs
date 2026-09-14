using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// The close action is thin by design: guard the request, call ICloseExerciseService, report. The
// sweep itself is pinned by CloseExerciseServiceTests — nothing here re-tests it.
public class CloseExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    private readonly ICloseExerciseService _closeService = Substitute.For<ICloseExerciseService>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

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

    private CloseExerciseController Build()
    {
        var controller = new CloseExerciseController(_closeService, _windowService)
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
        // A hand-typed URL must not offer to close an exercise this window never had.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_shows_the_preview_counts()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.PreviewAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExercisePreview { RequestsToClose = 4, DraftsToCancel = 2 });

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CloseExerciseViewModel>(view.Model);
        Assert.Equal(4, vm.RequestsToClose);
        Assert.Equal(2, vm.DraftsToCancel);
        Assert.Equal(WindowId, vm.WindowId);
        Assert.Equal(Exercise, vm.ExerciseType);
    }

    [Fact]
    public async Task Confirm_get_still_renders_when_there_is_nothing_to_close()
    {
        // Closing an already-swept exercise is harmless; the page says so rather than erroring.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.PreviewAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExercisePreview { RequestsToClose = 0, DraftsToCancel = 0 });

        var result = await Build().Confirm(WindowId, Exercise, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CloseExerciseViewModel>(view.Model);
        Assert.True(vm.HasNothingToDo);
    }

    [Fact]
    public async Task Close_post_runs_the_sweep_for_the_named_window_and_exercise()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1 });

        var result = await Build().Close(WindowId, Exercise, CancellationToken.None);

        await _closeService.Received(1).CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
    }

    [Fact]
    public async Task Close_post_reports_what_the_sweep_actually_did()
    {
        // The banner quotes the result, never the preview — rows can change in between.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _closeService.CloseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1 });

        var controller = Build();
        await controller.Close(WindowId, Exercise, CancellationToken.None);

        var message = Assert.IsType<string>(controller.TempData[CloseExerciseController.TempDataKey]);
        Assert.Contains("3", message);
        Assert.Contains("1", message);
    }

    [Fact]
    public async Task Close_post_does_not_sweep_an_exercise_the_window_does_not_run()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Close(WindowId, Exercise, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _closeService.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default);
    }
}
