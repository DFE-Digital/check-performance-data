using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class RemoveExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SummaryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly IWindowService _service = Substitute.For<IWindowService>();

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "16 to 19", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
        Exercises =
        [
            new CheckingExerciseDto { Id = SummaryId, ExerciseType = null, Name = "Summary data (Autumn)", TabName = "Summary",
                StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 2, 1), SortOrder = 2,
                Datasets =
                [
                    new CheckingWindowDatasetDto { Name = "summary", IngressFile = "summary.csv", SchemaFile = "summary.json" },
                    // A second slot holding only an ingress file: FileCount sums across every
                    // dataset's files individually, not one count per complete slot.
                    new CheckingWindowDatasetDto { Name = "late-summary", IngressFile = "late-summary.csv", SchemaFile = "" }
                ] }
        ]
    };

    private RemoveExerciseController Build() => new(_service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task Confirm_returns_not_found_for_an_unknown_exercise()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_names_the_exercise_and_counts_its_files()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, SummaryId, CancellationToken.None));
        var model = Assert.IsType<RemoveExerciseViewModel>(view.Model);

        Assert.Equal("Summary data (Autumn)", model.ExerciseLabel);
        Assert.Equal("16 to 19", model.WindowTitle);
        Assert.Equal(3, model.FileCount);
        Assert.Null(model.Refusal);
    }

    [Fact]
    public async Task Remove_calls_the_service_and_redirects_to_summary()
    {
        _service.RemoveExerciseAsync(WindowId, SummaryId, Arg.Any<CancellationToken>()).Returns(ExerciseChangeResult.Ok());

        var result = await Build().Remove(WindowId, SummaryId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Remove_redisplays_the_confirm_page_with_the_reason_when_refused()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _service.RemoveExerciseAsync(WindowId, SummaryId, Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("This exercise has change requests and cannot be removed"));

        var view = Assert.IsType<ViewResult>(await Build().Remove(WindowId, SummaryId, CancellationToken.None));

        Assert.Equal("This exercise has change requests and cannot be removed",
            Assert.IsType<RemoveExerciseViewModel>(view.Model).Refusal);
    }

    [Fact]
    public async Task Remove_redirects_to_summary_on_a_double_submit_rather_than_404ing_a_successful_removal()
    {
        // #466 review fix: pressing Remove twice removes the exercise on the first POST; the second
        // POST is refused "Exercise not found", and ConfirmView's own lookup would then find no
        // target and answer a successful removal with a 404.
        _service.RemoveExerciseAsync(WindowId, SummaryId, Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("Exercise not found"));

        var result = await Build().Remove(WindowId, SummaryId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
    }
}
