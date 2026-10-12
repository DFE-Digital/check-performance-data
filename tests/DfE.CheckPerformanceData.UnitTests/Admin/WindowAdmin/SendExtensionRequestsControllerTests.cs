using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#307773: the hand-over sweep for one CLOSED extension's schools, as SendExerciseRequestsController
// is for the exercise, then its audit row. The sweep is pinned by CloseExerciseServiceTests and the
// row by ExerciseExtensionRepositoryTests; this pins the gate, the order and what the banner says.
public sealed class SendExtensionRequestsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DataShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OpenExtensionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClosedExtensionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime ExerciseEnd = new(2026, 9, 20, 17, 0, 0);
    private static readonly DateTime ClosedExtensionEnd = new(2026, 9, 30, 17, 0, 0);

    private readonly ICloseExerciseService _closeService = Substitute.For<ICloseExerciseService>();
    private readonly IExerciseExtensionRepository _repository = Substitute.For<IExerciseExtensionRepository>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public SendExtensionRequestsControllerTests()
    {
        _currentUser.UserId.Returns("sub-1");
        _currentUser.DisplayName.Returns("Banks Jamgbadi");
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(_ => Window());
        _closeService.PreviewExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>())
            .Returns(new CloseExercisePreview { RequestsToClose = 3, DraftsToCancel = 1, RequestsWaiting = 2 });
        _closeService.CloseExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 3, DraftsCancelled = 1 });
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "KS4 June 2026",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 9, 1),
        EndDate = new DateTime(2026, 10, 8, 17, 0, 0),
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = ExerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                Name = "Autumn pupil data",
                StartDate = new DateTime(2026, 9, 1),
                EndDate = ExerciseEnd,
                Extensions =
                [
                    new CheckingExerciseExtensionDto
                    {
                        Id = OpenExtensionId, EndDate = new DateTime(2026, 10, 8, 17, 0, 0), Laestabs = ["9334290"]
                    },
                    new CheckingExerciseExtensionDto
                    {
                        Id = ClosedExtensionId, EndDate = ClosedExtensionEnd, Laestabs = ["1234567", "8604070"]
                    }
                ]
            },
            new CheckingExerciseDto
            {
                Id = DataShareId, ExerciseType = null, DisplayOnly = true,
                StartDate = new DateTime(2026, 9, 1), EndDate = ExerciseEnd, TabOrder = 1
            }
        ]
    };

    private SendExtensionRequestsController Build()
    {
        var clock = new FixedTimeProvider(Now);
        var controller = new SendExtensionRequestsController(
            _closeService, _repository, _windowService, new CheckingExerciseService(clock), _currentUser, clock,
            NullLogger<SendExtensionRequestsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private async Task NothingWasSent()
    {
        await _closeService.DidNotReceiveWithAnyArgs().CloseExtensionAsync(default, default, default, default);
        await _repository.DidNotReceiveWithAnyArgs().RecordRequestsSentAsync(default!, default);
    }

    private static void AssertRefusedNotClosed(Controller controller, IActionResult result)
    {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "This extension has not closed, so no requests were sent.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_get_returns_not_found_for_unknown_ids_and_a_data_share()
    {
        Assert.IsType<NotFoundResult>(await Build().Confirm(Guid.NewGuid(), ExerciseId, ClosedExtensionId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, ExerciseId, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, DataShareId, ClosedExtensionId, CancellationToken.None));
        await _closeService.DidNotReceiveWithAnyArgs().PreviewExtensionAsync(default, default, default, default);
    }

    [Fact]
    public async Task Confirm_get_refuses_an_extension_that_is_still_open()
    {
        // While it is open its schools can still add requests behind the sweep.
        var controller = Build();
        var result = await controller.Confirm(WindowId, ExerciseId, OpenExtensionId, CancellationToken.None);

        AssertRefusedNotClosed(controller, result);
        await _closeService.DidNotReceiveWithAnyArgs().PreviewExtensionAsync(default, default, default, default);
    }

    [Fact]
    public async Task Confirm_get_shows_what_the_sweep_would_touch()
    {
        var result = await Build().Confirm(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/SendExtensionRequests.cshtml", view.ViewName);
        var vm = Assert.IsType<SendExtensionRequestsViewModel>(view.Model);
        Assert.Equal("Autumn pupil data", vm.ExerciseLabel);
        Assert.Equal(ClosedExtensionEnd, vm.ExtensionEnd);
        Assert.Equal(2, vm.SchoolCount);
        Assert.Equal(3, vm.RequestsToSend);
        Assert.Equal(1, vm.DraftsToCancel);
        Assert.Equal(2, vm.RequestsWaiting);
        Assert.Equal(
            $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{ClosedExtensionId}/send-requests", vm.PostUrl);
    }

    // ── POST ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_post_returns_not_found_for_unknown_ids_and_a_data_share()
    {
        Assert.IsType<NotFoundResult>(await Build().Send(WindowId, ExerciseId, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Send(WindowId, DataShareId, ClosedExtensionId, CancellationToken.None));
        await NothingWasSent();
    }

    [Fact]
    public async Task Send_post_refuses_an_extension_that_is_still_open()
    {
        var controller = Build();
        var result = await controller.Send(WindowId, ExerciseId, OpenExtensionId, CancellationToken.None);

        AssertRefusedNotClosed(controller, result);
        await NothingWasSent();
    }

    [Fact]
    public async Task Send_post_sweeps_then_records_what_the_sweep_did_and_who_asked()
    {
        var controller = Build();
        var result = await controller.Send(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Received.InOrder(() =>
        {
            _closeService.CloseExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>());
            _repository.RecordRequestsSentAsync(Arg.Any<ExtensionRequestsSent>(), Arg.Any<CancellationToken>());
        });
        await _repository.Received(1).RecordRequestsSentAsync(
            Arg.Is<ExtensionRequestsSent>(r =>
                r.WindowId == WindowId && r.ExerciseId == ExerciseId
                && r.Exercise == CheckingExerciseType.PupilData
                && r.ExtensionId == ClosedExtensionId
                && r.ExtensionEnd == ClosedExtensionEnd
                && r.RequestsSent == 3 && r.DraftsCancelled == 1
                && r.Actor == new ExtensionActor("sub-1", "Banks Jamgbadi")
                && r.SentAtUtc == Now.UtcDateTime),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_post_quotes_the_result_in_the_success_banner()
    {
        var controller = Build();
        await controller.Send(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        Assert.Equal(
            "3 requests sent for processing and 1 draft cancelled for the extension of Autumn pupil data.",
            controller.TempData[CloseExerciseController.TempDataKey]);
    }

    [Fact]
    public async Task Send_post_says_how_many_requests_are_waiting()
    {
        _closeService.CloseExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = 1, DraftsCancelled = 0, Waiting = 2 });

        var controller = Build();
        await controller.Send(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        Assert.Equal(
            "1 request sent for processing and 0 drafts cancelled for the extension of Autumn pupil data. " +
            "2 requests are waiting for the Rules Engine. Send requests for processing again later.",
            controller.TempData[CloseExerciseController.TempDataKey]);
    }

    [Fact]
    public async Task Send_post_says_the_sweep_failed_and_records_nothing()
    {
        _closeService.CloseExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloseExerciseResult>(new InvalidOperationException("queue unavailable")));

        var controller = Build();
        var result = await controller.Send(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "The extension's requests could not be sent for processing. Select Send requests for processing to try again.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        Assert.Null(controller.TempData[CloseExerciseController.TempDataKey]);
        await _repository.DidNotReceiveWithAnyArgs().RecordRequestsSentAsync(default!, default);
    }

    [Fact]
    public async Task Send_post_does_not_swallow_a_cancelled_request()
    {
        _closeService.CloseExtensionAsync(WindowId, ExerciseId, ClosedExtensionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloseExerciseResult>(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Build().Send(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None));
    }
}
