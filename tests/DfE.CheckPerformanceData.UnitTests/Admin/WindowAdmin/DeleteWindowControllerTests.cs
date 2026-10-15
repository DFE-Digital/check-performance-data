using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// The controller is thin: guard the request, ask IDeleteWindowService, report. What a delete
// removes is pinned by DeleteWindowServiceTests and WindowDeletionRepositoryTests.
public class DeleteWindowControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IDeleteWindowService _deleteService = Substitute.For<IDeleteWindowService>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "KS4 June 2026",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30),
        Exercises =
        [
            new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 30)
            }
        ]
    };

    private static DeleteWindowPreview Preview(int submitted = 0) => new()
    {
        SubmittedNotSent = submitted,
        SentForProcessing = 0,
        Drafts = 0,
        WithdrawnOrCancelled = 0,
        EgressRuns = 0
    };

    private DeleteWindowController Build()
    {
        var controller = new DeleteWindowController(_deleteService, _windowService)
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
        _deleteService.PreviewAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Preview(submitted));
        _deleteService.DeleteAsync(WindowId, Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Confirm_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_shows_what_the_delete_takes_and_deletes_nothing()
    {
        GivenTheWindow(submitted: 3);

        var view = Assert.IsType<ViewResult>(await Build().Confirm(WindowId, CancellationToken.None));
        var model = Assert.IsType<DeleteWindowViewModel>(view.Model);

        Assert.Equal("KS4 June 2026", model.WindowTitle);
        Assert.Equal(1, model.ExerciseCount);
        Assert.Equal(3, model.Preview.SubmittedNotSent);
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task Delete_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        Assert.IsType<NotFoundResult>(await Build().Delete(WindowId, true, CancellationToken.None));
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task Delete_with_no_requests_needs_no_tick_and_goes_back_to_the_list()
    {
        GivenTheWindow();
        var controller = Build();

        var result = await controller.Delete(WindowId, confirmRequestsDeleted: false, CancellationToken.None);

        Assert.Equal("/admin/windows", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal("KS4 June 2026 deleted.", controller.TempData[DeleteWindowController.TempDataKey]);
        await _deleteService.Received(1).DeleteAsync(WindowId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_with_requests_and_no_tick_asks_again_and_deletes_nothing()
    {
        GivenTheWindow(submitted: 2);
        var controller = Build();

        var result = await controller.Delete(WindowId, confirmRequestsDeleted: false, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<DeleteWindowViewModel>(view.Model);
        Assert.True(controller.ModelState.ContainsKey(nameof(DeleteWindowViewModel.ConfirmRequestsDeleted)));
        await _deleteService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task Delete_with_requests_and_the_tick_deletes()
    {
        GivenTheWindow(submitted: 2);

        var result = await Build().Delete(WindowId, confirmRequestsDeleted: true, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        await _deleteService.Received(1).DeleteAsync(WindowId, Arg.Any<CancellationToken>());
    }
}
