using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public sealed class ExerciseReleaseControllerTests
{
    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly Guid ExerciseId = Guid.NewGuid();
    private static readonly Guid FirstId = Guid.NewGuid();
    private static readonly Guid SecondId = Guid.NewGuid();

    private readonly IWindowService _windows = Substitute.For<IWindowService>();
    private readonly ICheckingExerciseReleaseService _releases = Substitute.For<ICheckingExerciseReleaseService>();

    private ExerciseReleaseController Controller() => new(_windows, _releases);

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "16 to 19", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 1),
        KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = ExerciseId, ExerciseType = CheckingExerciseType.ResultsEnquiry, Name = "Results",
                StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 1),
                CurrentReleaseId = SecondId,
                Releases =
                [
                    new CheckingExerciseReleaseDto { Id = FirstId, Number = 1 },
                    new CheckingExerciseReleaseDto { Id = SecondId, Number = 2 }
                ]
            }
        ]
    };

    [Fact]
    public async Task Confirm_ShowsTheReleaseToGoLive_AndTheOneItReplaces()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var result = Assert.IsType<ViewResult>(await Controller().Confirm(WindowId, ExerciseId, FirstId, default));
        var model = Assert.IsType<MakeReleaseLiveViewModel>(result.Model);

        Assert.Equal(1, model.Release.Number);
        Assert.Equal(2, model.LiveRelease!.Number);
        Assert.Equal("Results", model.ExerciseName);
    }

    [Fact]
    public async Task Confirm_ForAReleaseOfNoExerciseHere_IsNotFound()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        Assert.IsType<NotFoundResult>(await Controller().Confirm(WindowId, ExerciseId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task MakeLive_RedirectsToTheExercisesReleases()
    {
        _releases.MakeLiveAsync(WindowId, ExerciseId, FirstId, Arg.Any<CancellationToken>())
            .Returns(MakeReleaseLiveResult.MadeLive);

        var result = Assert.IsType<RedirectResult>(await Controller().MakeLive(WindowId, ExerciseId, FirstId, default));

        Assert.Equal($"/admin/windows/{WindowId}/exercises/{ExerciseId}/edit#releases", result.Url);
    }

    [Fact]
    public async Task MakeLive_WhenTheServiceRefuses_IsNotFound()
    {
        _releases.MakeLiveAsync(WindowId, ExerciseId, FirstId, Arg.Any<CancellationToken>())
            .Returns(MakeReleaseLiveResult.NotFound);

        Assert.IsType<NotFoundResult>(await Controller().MakeLive(WindowId, ExerciseId, FirstId, default));
    }
}
