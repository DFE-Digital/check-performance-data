using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// The controller is thin: guard the request, ask IDeleteExerciseService, report. What a delete
// removes is pinned by DeleteExerciseServiceTests and ExerciseDeletionRepositoryTests.
public class DeleteExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PupilDataId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ShareId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IDeleteExerciseService _deleteService = Substitute.For<IDeleteExerciseService>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICheckingExerciseService _checkingExercises = Substitute.For<ICheckingExerciseService>();

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "16 to 19 2026",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30),
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = PupilDataId,
                ExerciseType = CheckingExerciseType.PupilData,
                Name = "Students",
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 30)
            },
            new CheckingExerciseDto
            {
                Id = ShareId,
                ExerciseType = null,
                Name = "Summary",
                DisplayOnly = true,
                ReplacesCheckingExerciseId = PupilDataId,
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 30)
            }
        ]
    };

    private static DeleteExercisePreview Preview(int submitted = 0) => new()
    {
        SubmittedNotSent = submitted,
        SentForProcessing = 0,
        Drafts = 0,
        WithdrawnOrCancelled = 0
    };

    private DeleteExerciseController Build()
    {
        var controller = new DeleteExerciseController(_deleteService, _windowService, _checkingExercises)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private void GivenTheWindow(int submitted = 0)
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _deleteService.PreviewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Preview(submitted));
        _deleteService.DeleteAsync(WindowId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Confirm_returns_not_found_for_an_exercise_of_another_window()
    {
        GivenTheWindow();

        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_shows_what_the_delete_takes_and_deletes_nothing()
    {
        GivenTheWindow(submitted: 3);
        _checkingExercises.StatusOf(Arg.Any<IReadOnlyList<CheckingExerciseDto>>(), Arg.Any<CheckingExerciseDto>())
            .Returns(ExerciseSchoolStatus.Visible);
        _checkingExercises.IsOpen(Arg.Any<CheckingExerciseDto>()).Returns(true);

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, PupilDataId, CancellationToken.None));
        var model = Assert.IsType<DeleteExerciseViewModel>(view.Model);

        Assert.Equal("Students", model.ExerciseName);
        Assert.Equal(3, model.Preview.SubmittedNotSent);
        Assert.True(model.IsVisibleToSchools);
        Assert.False(model.IsLastExercise);
        Assert.Equal(["Summary"], model.ReplacedBy);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/close", model.CloseLink);
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Fact]
    public async Task A_closed_exercise_offers_to_send_its_requests_instead_of_close()
    {
        // AB#301022: Close only acts on an open exercise. Once it has closed, the requests are
        // sent from their own page.
        GivenTheWindow(submitted: 3);
        _checkingExercises.HasClosed(Arg.Any<CheckingExerciseDto>()).Returns(true);

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, PupilDataId, CancellationToken.None));

        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/send-requests",
            Assert.IsType<DeleteExerciseViewModel>(view.Model).CloseLink);
    }

    [Fact]
    public async Task An_exercise_that_has_not_started_offers_neither()
    {
        GivenTheWindow();

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, PupilDataId, CancellationToken.None));

        Assert.Null(Assert.IsType<DeleteExerciseViewModel>(view.Model).CloseLink);
    }

    [Fact]
    public async Task A_data_share_offers_no_close()
    {
        GivenTheWindow();

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, ShareId, CancellationToken.None));

        Assert.Null(Assert.IsType<DeleteExerciseViewModel>(view.Model).CloseLink);
    }

    [Fact]
    public async Task Delete_returns_not_found_for_an_unknown_exercise()
    {
        GivenTheWindow();

        Assert.IsType<NotFoundResult>(await Build().Delete(WindowId, Guid.NewGuid(), true, CancellationToken.None));
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Fact]
    public async Task Delete_with_no_requests_needs_no_tick_and_goes_back_to_the_summary()
    {
        GivenTheWindow();
        var controller = Build();

        var result = await controller.Delete(WindowId, ShareId, confirmRequestsDeleted: false, CancellationToken.None);

        Assert.Equal($"/admin/windows/summary/{WindowId}", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal("Summary deleted.", controller.TempData[DeleteExerciseController.TempDataKey]);
        await _deleteService.Received(1).DeleteAsync(WindowId, ShareId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_with_requests_and_no_tick_asks_again_and_deletes_nothing()
    {
        GivenTheWindow(submitted: 2);
        var controller = Build();

        var result = await controller.Delete(WindowId, PupilDataId, confirmRequestsDeleted: false, CancellationToken.None);

        Assert.IsType<DeleteExerciseViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(controller.ModelState.ContainsKey(nameof(DeleteExerciseViewModel.ConfirmRequestsDeleted)));
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Fact]
    public async Task Delete_with_requests_and_the_tick_deletes()
    {
        GivenTheWindow(submitted: 2);

        var result = await Build().Delete(WindowId, PupilDataId, confirmRequestsDeleted: true, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        await _deleteService.Received(1).DeleteAsync(WindowId, PupilDataId, Arg.Any<CancellationToken>());
    }
}
