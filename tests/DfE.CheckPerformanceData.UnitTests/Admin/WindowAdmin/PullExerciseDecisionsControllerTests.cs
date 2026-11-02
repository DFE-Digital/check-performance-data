using System.Reflection;
using DfE.CheckPerformanceData.Application.ExerciseDecisions;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// Thin by design, like its send-requests sibling: guard the request, call the pull service,
// report on a banner. The bucketing and the decision rules are pinned by
// ExerciseDecisionPullServiceTests; the storage and the audit row by
// ExerciseDecisionPullRepositoryTests.
public sealed class PullExerciseDecisionsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    // Addressed by id, like Close and Send requests (#466): a window may hold several releases
    // of one kind.
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IExerciseDecisionPullService _pullService = Substitute.For<IExerciseDecisionPullService>();

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
            Id = e == Exercise ? ExerciseId : Guid.NewGuid(),
            ExerciseType = e,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 30),
            TabOrder = i
        }).ToList()
    };

    private PullExerciseDecisionsController Build()
    {
        var controller = new PullExerciseDecisionsController(_windowService, _pullService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static ExerciseDecisionPullResult Result(
        int accepted, int rejected, int unchanged, int noTicket, int ticketNotFound, string? failure = null) =>
        new()
        {
            Accepted = accepted, Rejected = rejected, Unchanged = unchanged,
            NoTicket = noTicket, TicketNotFound = ticketNotFound, FailureReason = failure
        };

    [Fact]
    public async Task Confirm_get_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns((CheckingWindowDto?)null);

        var result = await Build().Confirm(WindowId, ExerciseId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _pullService.DidNotReceiveWithAnyArgs().PreviewAsync(default, default, default);
    }

    [Fact]
    public async Task Confirm_get_returns_not_found_when_the_window_does_not_run_the_exercise()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Confirm(WindowId, ExerciseId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_returns_not_found_for_a_data_share()
    {
        // A data share has no kind, so no change request can belong to it.
        var window = Window();
        window.Exercises.Add(new CheckingExerciseDto
        {
            Id = ExerciseId, ExerciseType = null, DisplayOnly = true,
            StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
        });
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);

        var result = await Build().Confirm(WindowId, ExerciseId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Confirm_get_shows_the_preview_counts()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _pullService.PreviewAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(new ExerciseDecisionPullPreview(SubmittedTotal: 5, WithTicket: 4));

        var result = await Build().Confirm(WindowId, ExerciseId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/PullDecisions.cshtml", view.ViewName);
        var vm = Assert.IsType<PullDecisionsViewModel>(view.Model);
        Assert.Equal(5, vm.SubmittedTotal);
        Assert.Equal(4, vm.WithTicket);
        Assert.Equal("KS4 June 2026", vm.WindowTitle);
        Assert.Equal("Pupil data checking", vm.ExerciseLabel);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{ExerciseId}/pull-decisions", vm.PostUrl);
        Assert.Equal($"/admin/windows/summary/{WindowId}", vm.CancelLink);
    }

    [Fact]
    public async Task Pull_post_runs_the_pull_and_reports_what_it_did()
    {
        // The banner quotes the result, never the preview — rows can change in between. All five
        // counts are always shown, zeros included.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _pullService.PullAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(Result(2, 2, 1, 1, 0));

        var controller = Build();
        var result = await controller.Pull(WindowId, ExerciseId, CancellationToken.None);

        await _pullService.Received(1).PullAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "2 accepted, 2 rejected, 1 still under review, 1 with no ticket, 0 tickets not found, for Pupil data checking.",
            controller.TempData[PullExerciseDecisionsController.SuccessTempDataKey]);
    }

    [Fact]
    public async Task Pull_post_with_nothing_to_do_shows_the_all_zero_counts()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _pullService.PullAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(Result(0, 0, 0, 0, 0));

        var controller = Build();
        await controller.Pull(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(
            "0 accepted, 0 rejected, 0 still under review, 0 with no ticket, 0 tickets not found, for Pupil data checking.",
            controller.TempData[PullExerciseDecisionsController.SuccessTempDataKey]);
    }

    [Fact]
    public async Task Pull_post_shows_the_failure_reason_and_says_nothing_was_changed()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(Exercise));
        _pullService.PullAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(Result(0, 0, 0, 0, 0, failure: "Zendesk could not return tickets 1001."));

        var controller = Build();
        var result = await controller.Pull(WindowId, ExerciseId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Decisions could not be pulled for Pupil data checking. Zendesk could not return tickets 1001. No request was changed.",
            controller.TempData[PullExerciseDecisionsController.FailedTempDataKey]);
        Assert.False(controller.TempData.ContainsKey(PullExerciseDecisionsController.SuccessTempDataKey));
    }

    [Fact]
    public async Task Pull_post_rechecks_the_exercise_and_returns_not_found_when_it_is_gone()
    {
        // The confirmation page is not what authorises the pull, the route is: an exercise
        // deleted between the GET and the POST must not be pulled.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(Window(CheckingExerciseType.ResultsEnquiry));

        var result = await Build().Pull(WindowId, ExerciseId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _pullService.DidNotReceiveWithAnyArgs().PullAsync(default, default, default);
    }

    [Fact]
    public void Pull_post_requires_the_anti_forgery_token()
    {
        var method = typeof(PullExerciseDecisionsController).GetMethod(nameof(PullExerciseDecisionsController.Pull))!;
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
