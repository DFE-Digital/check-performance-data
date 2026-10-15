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

// AB#307773: the add and edit page of an extension. Thin by design: find the exercise, parse the
// pasted list, ask for the warnings, call IExerciseExtensionService, turn its answer into a field
// error or a banner. The rules themselves are pinned by ExerciseExtensionServiceTests and the
// parsing by LaestabListTests. Open or closed is asked of the real CheckingExerciseService on a
// fixed clock, as SummaryControllerCloseActionsTests does.
public sealed class ExerciseExtensionControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DataShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OpenExtensionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClosedExtensionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime ExerciseEnd = new(2026, 10, 1, 17, 0, 0);
    private static readonly DateTime OpenExtensionEnd = new(2026, 10, 8, 17, 0, 0);
    private static readonly DateTime ClosedExtensionEnd = new(2026, 9, 30, 17, 0, 0);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IExerciseExtensionService _extensions = Substitute.For<IExerciseExtensionService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public ExerciseExtensionControllerTests()
    {
        _currentUser.UserId.Returns("sub-1");
        _currentUser.DisplayName.Returns("Banks Jamgbadi");
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(_ => Window());
        // Every school has a file, and every save succeeds, unless a test says otherwise.
        _extensions.FindSchoolsWithoutDataAsync(default, default, default!, default)
            .ReturnsForAnyArgs(new ExtensionWarnings([], ExerciseHasNoRelease: false));
        _extensions.AddAsync(default!, default)
            .ReturnsForAnyArgs(new ExtensionSaveResult(ExtensionSaveStatus.Saved, Guid.NewGuid()));
        _extensions.EditAsync(default, default, default!, default)
            .ReturnsForAnyArgs(ci => new ExtensionSaveResult(ExtensionSaveStatus.Saved, ci.ArgAt<Guid>(0)));
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "KS4 June 2026",
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
                        Id = OpenExtensionId, EndDate = OpenExtensionEnd, Laestabs = ["9334290", "8604070"], Version = 2
                    },
                    new CheckingExerciseExtensionDto
                    {
                        Id = ClosedExtensionId, EndDate = ClosedExtensionEnd, Laestabs = ["1234567"]
                    }
                ]
            },
            new CheckingExerciseDto
            {
                Id = DataShareId,
                ExerciseType = null,
                DisplayOnly = true,
                StartDate = new DateTime(2026, 9, 1),
                EndDate = ExerciseEnd,
                TabOrder = 1
            }
        ]
    };

    private ExerciseExtensionController Build()
    {
        var controller = new ExerciseExtensionController(
            _windowService, _extensions,
            new CheckingExerciseService(new FixedTimeProvider(Now)), _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static ExerciseExtensionItem Form(string? schools = "933/4290\n8604070", bool confirm = false) => new()
    {
        EndDate = new DateTime(2026, 10, 8),
        EndHour = 17,
        EndMinute = 0,
        Schools = schools,
        ConfirmNoData = confirm
    };

    private static ExerciseExtensionItem EditForm(string? schools = "933/4290\n8604070") => new()
    {
        EndDate = new DateTime(2026, 10, 9),
        EndHour = 17,
        EndMinute = 0,
        Schools = schools,
        Version = 2
    };

    private static string[] Errors(Controller controller, string key) =>
        controller.ModelState.TryGetValue(key, out var entry)
            ? entry.Errors.Select(e => e.ErrorMessage).ToArray()
            : [];

    private static T PageModel<T>(IActionResult result) =>
        Assert.IsType<T>(Assert.IsType<ViewResult>(result).Model);

    private async Task NothingWasSaved()
    {
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _extensions.DidNotReceiveWithAnyArgs().EditAsync(default, default, default!, default);
    }

    private static void AssertRefused(Controller controller, IActionResult result, string banner)
    {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(banner, controller.TempData[SendExerciseRequestsController.RefusedTempDataKey]);
        Assert.Null(controller.TempData[ExerciseExtensionController.TempDataKey]);
    }

    // ── GET new ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task New_get_returns_not_found_for_a_data_share()
    {
        // A data share has no journey, so there is no time to extend.
        var result = await Build().New(WindowId, DataShareId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task New_get_returns_not_found_for_an_unknown_window_or_exercise()
    {
        Assert.IsType<NotFoundResult>(await Build().New(Guid.NewGuid(), ExerciseId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().New(WindowId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task New_get_shows_an_empty_form_ending_at_five_by_default()
    {
        var result = await Build().New(WindowId, ExerciseId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/WindowAdmin/ExerciseExtension.cshtml", view.ViewName);
        var vm = Assert.IsType<ExerciseExtensionItem>(view.Model);
        Assert.Equal(WindowId, vm.WindowId);
        Assert.Equal(ExerciseId, vm.ExerciseId);
        Assert.Null(vm.ExtensionId);
        Assert.Equal(ExerciseEnd, vm.ExerciseEnd);
        Assert.Equal(17, vm.EndHour);
        Assert.Null(vm.EndDate);
        Assert.Equal("Add an extension to Pupil data checking", vm.Heading);
        Assert.Equal("Add these schools anyway", vm.ConfirmLabel);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/new", vm.PostUrl);
    }

    // ── POST new: the form's own errors ──────────────────────────────────────

    [Fact]
    public async Task Create_post_with_a_line_that_is_not_a_laestab_redisplays_with_the_line_error()
    {
        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form("abc"), CancellationToken.None);

        var vm = PageModel<ExerciseExtensionItem>(result);
        Assert.Equal("abc", vm.Schools);                     // shown back so it can be corrected
        Assert.Equal(ExerciseEnd, vm.ExerciseEnd);           // not posted, so given again
        var error = Assert.Single(Errors(controller, "Schools"));
        Assert.Contains("Line 1: 'abc' is not a LAESTAB", error);
        await _extensions.DidNotReceiveWithAnyArgs().FindSchoolsWithoutDataAsync(default, default, default!, default);
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_gives_one_error_per_bad_line_in_line_order()
    {
        var controller = Build();
        await controller.Create(WindowId, ExerciseId, Form("933/4290\nabc\n\n12"), CancellationToken.None);

        Assert.Equal(
            [
                "Line 2: 'abc' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290.",
                "Line 4: '12' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290."
            ],
            Errors(controller, "Schools"));
        await NothingWasSaved();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \n")]
    public async Task Create_post_with_no_schools_asks_for_one(string? schools)
    {
        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(schools), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(["Enter at least one school"], Errors(controller, "Schools"));
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_with_a_date_error_still_checks_the_schools_and_saves_nothing()
    {
        // Every error at once: the admin should not fix the date only to be told about line 1.
        var controller = Build();
        controller.ModelState.AddModelError("EndDate", "Enter the extension end date");

        var result = await controller.Create(WindowId, ExerciseId, Form("abc"), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Single(Errors(controller, "EndDate"));
        Assert.Single(Errors(controller, "Schools"));
        await _extensions.DidNotReceiveWithAnyArgs().FindSchoolsWithoutDataAsync(default, default, default!, default);
        await NothingWasSaved();
    }

    // ── POST new: the warning step ───────────────────────────────────────────

    [Fact]
    public async Task Create_post_warns_of_schools_without_data_and_saves_nothing_until_confirmed()
    {
        _extensions.FindSchoolsWithoutDataAsync(WindowId, ExerciseId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionWarnings(["8604070"], ExerciseHasNoRelease: false));

        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        var vm = PageModel<ExerciseExtensionItem>(result);
        Assert.Equal(["860/4070"], vm.SchoolsWithoutData);
        Assert.False(vm.ExerciseHasNoRelease);
        Assert.True(vm.HasWarning);
        // An error on the checkbox, so the page announces that nothing was saved (WCAG 3.3.1).
        Assert.Equal(
            ["Confirm that you want to add these schools, or remove them from the list"],
            Errors(controller, "ConfirmNoData"));
        Assert.Equal(1, controller.ModelState.ErrorCount);
        await _extensions.Received(1).FindSchoolsWithoutDataAsync(
            WindowId, ExerciseId,
            Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "9334290", "8604070" })),
            Arg.Any<CancellationToken>());
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_says_when_the_exercise_has_no_release_yet()
    {
        _extensions.FindSchoolsWithoutDataAsync(WindowId, ExerciseId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionWarnings(["9334290", "8604070"], ExerciseHasNoRelease: true));

        var result = await Build().Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        var vm = PageModel<ExerciseExtensionItem>(result);
        Assert.True(vm.ExerciseHasNoRelease);
        Assert.Equal(["933/4290", "860/4070"], vm.SchoolsWithoutData);
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_sent_again_without_the_tick_is_refused_again_with_the_error()
    {
        // The second press is the one a screen-reader user is most likely to think saved.
        _extensions.FindSchoolsWithoutDataAsync(WindowId, ExerciseId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionWarnings(["8604070"], ExerciseHasNoRelease: false));

        await Build().Create(WindowId, ExerciseId, Form(confirm: false), CancellationToken.None);
        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(confirm: false), CancellationToken.None);

        Assert.True(PageModel<ExerciseExtensionItem>(result).HasWarning);
        Assert.Single(Errors(controller, "ConfirmNoData"));
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_confirmed_saves_the_schools_as_digits_and_says_so()
    {
        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(confirm: true), CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/admin/windows/summary/{WindowId}", redirect.Url);
        Assert.Equal(
            "Extension added for 2 schools, ending 08/10/2026 17:00.",
            controller.TempData[ExerciseExtensionController.TempDataKey]);
        // Confirmed, so the warning is not asked again.
        await _extensions.DidNotReceiveWithAnyArgs().FindSchoolsWithoutDataAsync(default, default, default!, default);
        await _extensions.Received(1).AddAsync(
            Arg.Is<ExtensionCommand>(c =>
                c.WindowId == WindowId && c.ExerciseId == ExerciseId
                && c.EndDate == OpenExtensionEnd
                && c.Laestabs.SequenceEqual(new[] { "9334290", "8604070" })
                && c.Actor == new ExtensionActor("sub-1", "Banks Jamgbadi")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_post_with_no_warnings_saves_without_asking()
    {
        var controller = Build();
        await controller.Create(WindowId, ExerciseId, Form("9334290", confirm: false), CancellationToken.None);

        Assert.Equal(
            "Extension added for 1 school, ending 08/10/2026 17:00.",
            controller.TempData[ExerciseExtensionController.TempDataKey]);
        await _extensions.ReceivedWithAnyArgs(1).AddAsync(default!, default);
    }

    // ── POST new: the service's refusals ─────────────────────────────────────

    [Fact]
    public async Task Create_post_ending_on_or_before_the_exercise_end_is_a_date_error()
    {
        _extensions.AddAsync(Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.EndDateNotAfterExercise, ExerciseEnd: ExerciseEnd));

        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(
            ["The extension must end after the exercise ends on 01/10/2026 17:00"],
            Errors(controller, "EndDate"));
        Assert.Null(controller.TempData[ExerciseExtensionController.TempDataKey]);
    }

    [Fact]
    public async Task Create_post_ending_in_the_past_is_a_date_error()
    {
        // AB#307773 review I1.
        _extensions.AddAsync(Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.EndDateNotInFuture));

        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(["The extension end date must be in the future"], Errors(controller, "EndDate"));
        Assert.Null(controller.TempData[ExerciseExtensionController.TempDataKey]);
    }

    [Fact]
    public async Task Create_post_naming_a_school_in_another_extension_says_which_and_when_it_ends()
    {
        _extensions.AddAsync(Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(
                ExtensionSaveStatus.SchoolInAnotherExtension,
                Conflicts: [new ExtensionConflict("9334290", OpenExtensionEnd), new ExtensionConflict("8604070", OpenExtensionEnd)]));

        var controller = Build();
        var result = await controller.Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(
            [
                "933/4290 is already in the extension ending 08/10/2026 17:00.",
                "860/4070 is already in the extension ending 08/10/2026 17:00."
            ],
            Errors(controller, "Schools"));
    }

    [Fact]
    public async Task Create_post_the_service_finds_no_schools_in_asks_for_one()
    {
        _extensions.AddAsync(Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.NoSchools));

        var controller = Build();
        await controller.Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        Assert.Equal(["Enter at least one school"], Errors(controller, "Schools"));
    }

    [Fact]
    public async Task Create_post_for_a_data_share_is_not_found_and_saves_nothing()
    {
        var result = await Build().Create(WindowId, DataShareId, Form(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await NothingWasSaved();
    }

    [Fact]
    public async Task Create_post_the_service_cannot_find_is_not_found()
    {
        _extensions.AddAsync(Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.NotFound));

        var result = await Build().Create(WindowId, ExerciseId, Form(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── GET edit ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit_get_of_a_closed_extension_goes_back_with_a_neutral_banner()
    {
        var controller = Build();
        var result = await controller.Edit(WindowId, ExerciseId, ClosedExtensionId, CancellationToken.None);

        AssertRefused(controller, result, "This extension has closed and cannot be changed.");
    }

    [Fact]
    public async Task Edit_get_of_an_open_extension_shows_it_with_the_schools_slashed()
    {
        var result = await Build().Edit(WindowId, ExerciseId, OpenExtensionId, CancellationToken.None);

        var vm = PageModel<ExerciseExtensionItem>(result);
        Assert.Equal(OpenExtensionId, vm.ExtensionId);
        Assert.True(vm.IsEdit);
        Assert.Equal("Edit extension", vm.Heading);
        Assert.Equal("933/4290\n860/4070", vm.Schools);
        Assert.Equal(2, vm.Version);
        Assert.Equal(OpenExtensionEnd.Date, vm.EndDate);
        Assert.Equal(17, vm.EndHour);
        Assert.Equal(0, vm.EndMinute);
        Assert.Equal(ExerciseEnd, vm.ExerciseEnd);
        Assert.Equal(
            $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{OpenExtensionId}/edit", vm.PostUrl);
    }

    [Fact]
    public async Task Edit_get_returns_not_found_for_an_unknown_extension_or_a_data_share()
    {
        Assert.IsType<NotFoundResult>(await Build().Edit(WindowId, ExerciseId, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build().Edit(WindowId, DataShareId, OpenExtensionId, CancellationToken.None));
    }

    // ── POST edit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_post_of_a_closed_extension_saves_nothing()
    {
        // Re-checked on the POST: a page left open across the end date still posts here.
        var controller = Build();
        var form = EditForm();

        var result = await controller.Update(WindowId, ExerciseId, ClosedExtensionId, form, CancellationToken.None);

        AssertRefused(controller, result, "This extension has closed and cannot be changed.");
        await NothingWasSaved();
    }

    [Fact]
    public async Task Update_post_saves_against_the_version_the_page_showed_and_says_so()
    {
        var controller = Build();
        var result = await controller.Update(WindowId, ExerciseId, OpenExtensionId, EditForm(), CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(
            "Extension updated for 2 schools, ending 09/10/2026 17:00.",
            controller.TempData[ExerciseExtensionController.TempDataKey]);
        await _extensions.Received(1).EditAsync(
            OpenExtensionId, 2,
            Arg.Is<ExtensionCommand>(c =>
                c.WindowId == WindowId && c.ExerciseId == ExerciseId
                && c.EndDate == new DateTime(2026, 10, 9, 17, 0, 0)
                && c.Laestabs.SequenceEqual(new[] { "9334290", "8604070" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_post_changed_by_someone_else_goes_back_with_a_neutral_banner()
    {
        _extensions.EditAsync(OpenExtensionId, Arg.Any<int>(), Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.Stale));

        var controller = Build();
        var result = await controller.Update(WindowId, ExerciseId, OpenExtensionId, EditForm(), CancellationToken.None);

        AssertRefused(controller, result, "This extension was changed by someone else. Check it and try again.");
    }

    [Fact]
    public async Task Update_post_the_service_finds_closed_goes_back_with_the_closed_banner()
    {
        // It closed between the controller's check and the service's.
        _extensions.EditAsync(OpenExtensionId, Arg.Any<int>(), Arg.Any<ExtensionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSaveResult(ExtensionSaveStatus.Closed));

        var controller = Build();
        var result = await controller.Update(WindowId, ExerciseId, OpenExtensionId, EditForm(), CancellationToken.None);

        AssertRefused(controller, result, "This extension has closed and cannot be changed.");
    }

    [Fact]
    public async Task Update_post_warns_with_the_save_wording_and_keeps_the_guard()
    {
        _extensions.FindSchoolsWithoutDataAsync(WindowId, ExerciseId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionWarnings(["8604070"], ExerciseHasNoRelease: false));

        var controller = Build();
        var result = await controller.Update(WindowId, ExerciseId, OpenExtensionId, EditForm(), CancellationToken.None);

        var vm = PageModel<ExerciseExtensionItem>(result);
        Assert.Equal(["860/4070"], vm.SchoolsWithoutData);
        Assert.Equal("Save these schools anyway", vm.ConfirmLabel);
        Assert.Equal(
            ["Confirm that you want to save these schools, or remove them from the list"],
            Errors(controller, "ConfirmNoData"));
        Assert.Equal(2, vm.Version);  // posted again with the confirmation
        await NothingWasSaved();
    }

    [Fact]
    public async Task Update_post_without_the_version_it_was_shown_is_a_bad_request()
    {
        // The page always posts it; without it there is nothing to guard the save with.
        var form = EditForm();
        form.Version = null;

        var result = await Build().Update(WindowId, ExerciseId, OpenExtensionId, form, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await NothingWasSaved();
    }

    [Fact]
    public async Task Edit_get_shows_the_extensions_version_for_the_post_to_send_back()
    {
        var result = await Build().Edit(WindowId, ExerciseId, OpenExtensionId, CancellationToken.None);

        Assert.Equal(2, PageModel<ExerciseExtensionItem>(result).Version);
    }

    [Fact]
    public async Task Update_post_returns_not_found_for_an_unknown_extension()
    {
        var result = await Build().Update(WindowId, ExerciseId, Guid.NewGuid(), EditForm(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await NothingWasSaved();
    }
}
