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

// #466: validate is keyed by exercise id and refuses an exercise that is not ready — either
// display-only (no kind, no blob prefix until slice 2) or a kind exercise still missing a required
// file. The processor's own behaviour once called is pinned by CsvSchemaFileProcessor's own tests;
// what belongs here is only this controller's guard and its target.ExerciseType!.Value derivation.
public class ValidateWindowControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PupilDataId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SummaryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid IncompleteId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICsvSchemaFileProcessor _processor = Substitute.For<ICsvSchemaFileProcessor>();

    private static CheckingWindowDatasetDto Complete(string name) => new()
    {
        Name = name, IngressFile = $"{name}.csv", SchemaFile = $"{name}.json"
    };

    // Required (the default) but missing its schema file — HasRequiredFiles is false for the
    // exercise that holds only this.
    private static CheckingWindowDatasetDto Incomplete(string name) => new()
    {
        Name = name, IngressFile = $"{name}.csv"
    };

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "w",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2027, 1, 1),
        EndDate = new DateTime(2027, 6, 1),
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = PupilDataId,
                ExerciseType = CheckingExerciseType.PupilData,
                Name = "Pupil data checking",
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 14),
                Datasets = [Complete("included"), Complete("nonincluded")]
            },
            new CheckingExerciseDto
            {
                Id = SummaryId,
                ExerciseType = null,
                Name = "Summary data (Autumn)",
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 2, 1),
                SortOrder = 2,
                Datasets = [Complete("summary")]
            },
            new CheckingExerciseDto
            {
                Id = IncompleteId,
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                Name = "Results enquiry",
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 3, 1),
                SortOrder = 3,
                Datasets = [Incomplete("main")]
            }
        ]
    };

    private ValidateWindowController Build()
    {
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
        return new ValidateWindowController(_windowService, _processor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = urlHelper
        };
    }

    [Fact]
    public async Task Index_returns_not_found_for_an_exercise_the_window_does_not_have()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var result = await Build().Index(WindowId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Index_returns_bad_request_for_a_display_only_exercise()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var result = await Build().Index(WindowId, SummaryId, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Index_shows_the_exercises_own_name()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var view = Assert.IsType<ViewResult>(await Build().Index(WindowId, PupilDataId, CancellationToken.None));

        Assert.Equal("Pupil data checking", Assert.IsType<ValidationViewModel>(view.Model).ExerciseLabel);
    }

    [Fact]
    public async Task Validate_post_never_runs_the_processor_for_a_display_only_exercise()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        await Build().Validate(WindowId, SummaryId, CancellationToken.None);

        _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default!, cancellationToken: default);
    }

    [Fact]
    public async Task Index_returns_bad_request_for_a_kind_exercise_missing_a_required_file()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var result = await Build().Index(WindowId, IncompleteId, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Validate_post_never_runs_the_processor_for_a_kind_exercise_missing_a_required_file()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        await Build().Validate(WindowId, IncompleteId, CancellationToken.None);

        _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default!, cancellationToken: default);
    }

    [Fact]
    public async Task Validate_post_reaches_the_processor_for_a_kind_exercise_with_complete_files()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _processor.ProcessAsync(
                WindowId,
                CheckingExerciseType.PupilData,
                Arg.Any<IReadOnlyList<IngressDataset>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(RunToCompletion());

        await Build().Validate(WindowId, PupilDataId, CancellationToken.None);

        _processor.Received(1).ProcessAsync(
            WindowId,
            CheckingExerciseType.PupilData,
            Arg.Is<IReadOnlyList<IngressDataset>>(d => d.Select(x => x.Name).SequenceEqual(new[] { "included", "nonincluded" })),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ValidationProgress> RunToCompletion()
    {
        yield return new ValidationProgress(
            Phase: "done", Message: "ok", RecordsRead: 1, RecordsProcessed: 1, FilesWritten: 1, ErrorCount: 0,
            IsComplete: true, IsError: false);
        await Task.CompletedTask;
    }
}
