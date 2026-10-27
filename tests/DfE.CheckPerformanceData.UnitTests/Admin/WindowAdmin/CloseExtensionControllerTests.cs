using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#307773: Close early ends an OPEN extension now, once the admin has typed the window name, as
// CloseExerciseController does for the exercise. Unlike the exercise's Close it runs no sweep: the
// extension's requests go by Send requests or the automatic hand-over. The close's own checks are
// pinned by ExerciseExtensionServiceTests; open or closed is asked of the real service on a fixed clock.
public sealed class CloseExtensionControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DataShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OpenExtensionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClosedExtensionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string WindowTitle = "KS4 June 2026";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime ExerciseEnd = new(2026, 10, 1, 17, 0, 0);
    private static readonly DateTime OpenExtensionEnd = new(2026, 10, 8, 17, 0, 0);
    private static readonly DateTime ClosedAt = new(2026, 10, 1, 12, 0, 0);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IExerciseExtensionService _extensions = Substitute.For<IExerciseExtensionService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public CloseExtensionControllerTests()
    {
        _currentUser.UserId.Returns("sub-1");
        _currentUser.DisplayName.Returns("Banks Jamgbadi");
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(_ => Window());
        _extensions.CloseEarlyAsync(WindowId, ExerciseId, OpenExtensionId, Arg.Any<ExtensionActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.Closed(ClosedAt, OpenExtensionEnd));
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = WindowTitle,
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 9, 1),
        EndDate = OpenExtensionEnd,
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = ExerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2026, 9, 1),
                EndDate = ExerciseEnd,
                Extensions =
                [
                    new CheckingExerciseExtensionDto
                    {
                        Id = OpenExtensionId, EndDate = OpenExtensionEnd, Laestabs = ["9334290", "8604070"]
                    },
                    new CheckingExerciseExtensionDto
                    {
                        Id = ClosedExtensionId, EndDate = new DateTime(2026, 9, 30, 17, 0, 0), Laestabs = ["1234567"]
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

    private CloseExtensionController Build()
    {
        var controller = new CloseExtensionController(
            _extensions, _windowService, new CheckingExerciseService(new FixedTimeProvider(Now)), _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private async Task NothingWasClosed() =>
        await _extensions.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default, default, default, default!, default);

    private static void AssertRefusedNotOpen(Controller controller, IActionResult result)
    {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "This extension is not open, so it was not closed.",
            controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        Assert.Null(controller.TempData[ExerciseExtensionController.TempDataKey]);
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_get_returns_not_found_for_unknown_ids_and_a_data_share()
    {
        Assert.IsType<NotFoundResult>(await Build().Confirm(Guid.NewGuid(), ExerciseId, OpenExtensionId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, Guid.NewGuid(), OpenExtensionId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, ExerciseId, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Confirm(WindowId, DataShareId, OpenExtensionId, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_get_refuses_an_extension_that_has_closed()
    {
        var controller = Build();
        var result = await controller.Confirm(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        AssertRefusedNotOpen(controller, result);
    }

    [Fact]
    public async Task Confirm_get_shows_the_extension_its_scheduled_end_and_its_schools()
    {
        var result = await Build().Confirm(WindowId, ExerciseId, OpenExtensionId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/CloseExtension.cshtml", view.ViewName);
        var vm = Assert.IsType<CloseExtensionViewModel>(view.Model);
        Assert.Equal(WindowTitle, vm.WindowTitle);
        Assert.Equal("Pupil data checking", vm.ExerciseLabel);
        Assert.Equal(OpenExtensionEnd, vm.ScheduledEnd);
        Assert.Equal("08/10/2026", vm.ScheduledEndDate);
        Assert.Equal("17:00", vm.ScheduledEndTime);
        Assert.Equal(2, vm.SchoolCount);
        Assert.False(vm.HasError);
        Assert.Equal(
            $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{OpenExtensionId}/close", vm.PostUrl);
    }

    // ── POST ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Close_post_returns_not_found_for_unknown_ids_and_a_data_share()
    {
        Assert.IsType<NotFoundResult>(await Build().Close(WindowId, ExerciseId, Guid.NewGuid(), WindowTitle, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Close(WindowId, DataShareId, OpenExtensionId, WindowTitle, CancellationToken.None));
        await NothingWasClosed();
    }

    [Fact]
    public async Task Close_post_refuses_a_closed_extension_whatever_was_typed()
    {
        var controller = Build();
        var result = await controller.Close(WindowId, ExerciseId, ClosedExtensionId, WindowTitle, CancellationToken.None);

        AssertRefusedNotOpen(controller, result);
        await NothingWasClosed();
    }

    [Theory]
    [InlineData(null, "Enter the window name")]
    [InlineData("  ", "Enter the window name")]
    [InlineData("ks4 june 2026", "The window name you entered does not match")]
    public async Task Close_post_without_the_exact_window_name_closes_nothing_and_says_why(string? typed, string error)
    {
        var result = await Build().Close(WindowId, ExerciseId, OpenExtensionId, typed, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/CloseExtension.cshtml", view.ViewName);
        var vm = Assert.IsType<CloseExtensionViewModel>(view.Model);
        Assert.Equal(error, vm.Error);
        Assert.Equal(typed, vm.ConfirmWindowName);
        await NothingWasClosed();
    }

    [Fact]
    public async Task Close_post_with_the_exact_name_closes_the_extension_and_says_when_and_by_whom()
    {
        var controller = Build();
        var result = await controller.Close(WindowId, ExerciseId, OpenExtensionId, "  KS4 June 2026 ", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Extension of Pupil data checking ending 08/10/2026, 17:00 was closed early on 01/10/2026, 12:00 by Banks Jamgbadi.",
            controller.TempData[ExerciseExtensionController.TempDataKey]);
        await _extensions.Received(1).CloseEarlyAsync(
            WindowId, ExerciseId, OpenExtensionId,
            new ExtensionActor("sub-1", "Banks Jamgbadi"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Close_post_the_service_finds_not_open_is_refused()
    {
        // A second admin closed it, or its end passed, between the page and the press.
        _extensions.CloseEarlyAsync(WindowId, ExerciseId, OpenExtensionId, Arg.Any<ExtensionActor>(), Arg.Any<CancellationToken>())
            .Returns(EarlyClosureResult.NotOpen);

        var controller = Build();
        var result = await controller.Close(WindowId, ExerciseId, OpenExtensionId, WindowTitle, CancellationToken.None);

        AssertRefusedNotOpen(controller, result);
    }
}
