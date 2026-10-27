using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #466 slice 2: the exercise-id addressed routes sit beside the existing kind-addressed ones.
// The kind route's own behaviour (ICsvSchemaFileProcessor + manual stamping) is unchanged and is
// not re-tested here; these tests cover only what slice 2 adds — running by exercise id through
// ICheckingExerciseIngress, and refusing an id that is not this window's.
public class ValidateWindowControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICsvSchemaFileProcessor _processor = Substitute.For<ICsvSchemaFileProcessor>();
    private readonly ICheckingExerciseIngress _ingress = Substitute.For<ICheckingExerciseIngress>();
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ValidateWindowControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    private ValidateWindowController Controller()
    {
        var controller = new ValidateWindowController(_windowService, _processor, _ingress)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = _urlHelper
        };
        return controller;
    }

    private static CheckingWindowDto Window(params CheckingExerciseDto[] exercises) => new()
    {
        Id = WindowId,
        Title = "KS4 June 2026",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30),
        Exercises = exercises.ToList()
    };

    private static CheckingExerciseDto Exercise(Guid id, CheckingExerciseType type = CheckingExerciseType.PupilData) => new()
    {
        Id = id,
        ExerciseType = type,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30)
    };

    private static async IAsyncEnumerable<ValidationProgress> Progress(ValidationProgress progress)
    {
        yield return progress;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RunsTheExerciseTheRouteNames_ThroughTheExerciseIngress()
    {
        var exerciseId = Guid.NewGuid();
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(exerciseId)));
        _ingress.ProcessAsync(exerciseId, false, Arg.Any<CancellationToken>())
            .Returns(Progress(new ValidationProgress("Done", "ok", 1, 1, 1, 0, true, false)));

        await Controller().Run(WindowId, exerciseId, clearExistingFiles: false, CancellationToken.None);

        _ingress.Received(1).ProcessAsync(exerciseId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunsExactlyOneExercise_WithExactlyOneTerminalEvent()
    {
        // Two progress events, only the last is terminal — the same shape the kind route already
        // relies on. There must be exactly one call into the ingress (never a loop over exercises).
        var exerciseId = Guid.NewGuid();
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(exerciseId)));

        static async IAsyncEnumerable<ValidationProgress> TwoSteps()
        {
            yield return new ValidationProgress("Reading", "reading", 1, 0, 0, 0, false, false);
            await Task.CompletedTask;
            yield return new ValidationProgress("Done", "done", 1, 1, 1, 0, true, false);
        }

        _ingress.ProcessAsync(exerciseId, false, Arg.Any<CancellationToken>()).Returns(TwoSteps());

        var result = await Controller().Run(WindowId, exerciseId, clearExistingFiles: false, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ValidationViewModel>(view.Model);
        Assert.NotNull(model.ProcessingResult);
        Assert.Equal("done", model.ProcessingResult!.ErrorLogs.ToString());
        _ingress.Received(1).ProcessAsync(exerciseId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExerciseThatIsNotOnThisWindow_IsNotFound()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(Guid.NewGuid())));

        var result = await Controller().Run(WindowId, Guid.NewGuid(), false, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        _ingress.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
    }

    [Fact]
    public async Task AnExerciseIdFromAnUnknownWindow_IsNotFound()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns((CheckingWindowDto?)null);

        var result = await Controller().Run(WindowId, Guid.NewGuid(), false, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task StreamForExercise_IsNotFound_ForAnExerciseIdNotOnThisWindow()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(Guid.NewGuid())));

        var result = await Controller().StreamForExercise(WindowId, Guid.NewGuid(), false, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task IndexForExercise_IsNotFound_ForAnExerciseIdNotOnThisWindow()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(Guid.NewGuid())));

        var result = await Controller().IndexForExercise(WindowId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task IndexForExercise_BuildsAViewModel_ForAKnownExercise()
    {
        var exerciseId = Guid.NewGuid();
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(exerciseId, CheckingExerciseType.ResultsEnquiry)));

        var result = await Controller().IndexForExercise(WindowId, exerciseId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ValidationViewModel>(view.Model);
        Assert.Equal(WindowId, model.WindowId);
    }

    // --- The existing kind route must still work unchanged (nothing already linked to it 404s) ---

    [Fact]
    public void Index_ByKind_StillReturnsTheValidationView()
    {
        IActionResult result = Controller().Index(WindowId, CheckingExerciseType.PupilData);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ValidationViewModel>(view.Model);
        Assert.Equal(WindowId, model.WindowId);
        Assert.False(string.IsNullOrEmpty(model.ExerciseLabel));
    }

    [Fact]
    public async Task Validate_ByKind_StillRunsThroughTheCsvSchemaFileProcessor()
    {
        // The kind route's own behaviour is out of scope for slice 2 and must not have moved onto
        // the exercise ingress.
        var exerciseId = Guid.NewGuid();
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(Exercise(exerciseId, CheckingExerciseType.PupilData)));
        _processor.ProcessAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<IReadOnlyList<IngressDataset>>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>())
            .Returns(Progress(new ValidationProgress("Done", "ok", 1, 1, 1, 0, true, false)));

        var result = await Controller().Validate(WindowId, CheckingExerciseType.PupilData, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        _processor.Received(1).ProcessAsync(WindowId, CheckingExerciseType.PupilData,
            Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>());
        _ingress.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
    }
}
