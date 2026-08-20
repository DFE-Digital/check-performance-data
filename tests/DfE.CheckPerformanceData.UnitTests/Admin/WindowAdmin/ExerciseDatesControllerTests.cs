using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: replaces StartDateControllerTests and EndDateControllerTests. There is no window-level date
// step any more — an exercise's dates are captured on one page, and the window's own pair is derived
// from them as their union, so the two can never disagree.
public class ExerciseDatesControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ExerciseDatesControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    // ── New (draft) ──────────────────────────────────────────────────────────

    [Fact]
    public void New_get_returns_bad_request_when_no_session_data()
    {
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = Substitute.For<ISession>() });

        IActionResult result = controller.New(CheckingExerciseType.PupilData);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public void New_get_returns_not_found_for_an_exercise_the_draft_does_not_run()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = SessionWithDraft(draft) });

        Assert.IsType<NotFoundResult>(controller.New(CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void New_get_returns_the_view_labelled_with_the_exercise()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.ResultsEnquiry);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(controller.New(CheckingExerciseType.ResultsEnquiry));
        ExerciseDatesItem model = Assert.IsType<ExerciseDatesItem>(view.Model);

        Assert.Equal(CheckingExerciseType.ResultsEnquiry, model.ExerciseType);
        Assert.Equal("Results enquiry", model.ExerciseLabel);
        Assert.Equal(Guid.Empty, model.WindowId);
    }

    [Fact]
    public void New_get_defaults_a_new_exercise_to_midnight_and_five_pm()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(controller.New(CheckingExerciseType.PupilData));
        ExerciseDatesItem model = Assert.IsType<ExerciseDatesItem>(view.Model);

        Assert.Equal(0, model.StartHour);
        Assert.Equal(17, model.EndHour);
    }

    [Fact]
    public void New_post_stores_both_dates_on_the_exercise_and_redirects()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ISession session = SessionWithDraft(draft);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = session });
        controller.Url = _urlHelper;

        DateTime start = DateTime.UtcNow.AddMonths(1).Date;
        DateTime end = DateTime.UtcNow.AddMonths(2).Date;

        IActionResult result = controller.Submit(CheckingExerciseType.PupilData, new ExerciseDatesItem
        {
            StartDate = start, StartHour = 9, StartMinute = 30,
            EndDate = end, EndHour = 17, EndMinute = 0
        });

        Assert.IsType<RedirectResult>(result);
        CheckingWindowDraft saved = SavedDraft(session);
        ExerciseDraft exercise = Assert.Single(saved.Exercises);
        Assert.Equal(start.AddHours(9).AddMinutes(30), exercise.StartDate);
        Assert.Equal(end.AddHours(17), exercise.EndDate);
    }

    [Fact]
    public void New_post_rejects_a_start_date_in_the_past()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        IActionResult result = controller.Submit(CheckingExerciseType.PupilData, new ExerciseDatesItem
        {
            StartDate = new DateTime(2020, 1, 1),
            EndDate = DateTime.UtcNow.AddMonths(1).Date
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
    }

    [Fact]
    public void New_post_rejects_an_end_date_before_the_start_date()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(Substitute.For<IWindowService>(),
            new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        IActionResult result = controller.Submit(CheckingExerciseType.PupilData, new ExerciseDatesItem
        {
            StartDate = DateTime.UtcNow.AddMonths(2).Date,
            EndDate = DateTime.UtcNow.AddMonths(1).Date
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
    }

    // ── Edit (existing window) ───────────────────────────────────────────────

    [Fact]
    public async Task Edit_get_returns_not_found_when_the_window_does_not_run_the_exercise()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        ExerciseDatesController controller = Build(windowService, new DefaultHttpContext());

        Assert.IsType<NotFoundResult>(
            await controller.Edit(WindowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None));
    }

    [Fact]
    public async Task Edit_get_returns_the_exercises_own_dates_not_the_windows()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        ExerciseDatesController controller = Build(windowService, new DefaultHttpContext());
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(
            await controller.Edit(WindowId, CheckingExerciseType.PupilData, CancellationToken.None));
        ExerciseDatesItem model = Assert.IsType<ExerciseDatesItem>(view.Model);

        Assert.Equal(new DateTime(2027, 1, 1), model.StartDate);
        Assert.Equal(new DateTime(2027, 1, 15, 17, 0, 0), model.EndDate);
        Assert.Equal(WindowId, model.WindowId);
    }

    [Fact]
    public async Task Edit_post_updates_the_exercise_and_redirects_to_summary()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        CheckingWindowDto window = Window();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);

        ExerciseDatesController controller = Build(windowService, new DefaultHttpContext());
        controller.Url = _urlHelper;

        DateTime start = DateTime.UtcNow.AddMonths(1).Date;
        DateTime end = DateTime.UtcNow.AddMonths(3).Date;

        IActionResult result = await controller.Update(WindowId, CheckingExerciseType.PupilData,
            new ExerciseDatesItem { StartDate = start, StartHour = 8, EndDate = end, EndHour = 17 },
            CancellationToken.None);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(start.AddHours(8), window.FindExercise(CheckingExerciseType.PupilData)!.StartDate);
        await windowService.Received(1).UpdateAsync(window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_post_does_not_save_a_start_date_in_the_past()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        ExerciseDatesController controller = Build(windowService, new DefaultHttpContext());
        controller.Url = _urlHelper;

        IActionResult result = await controller.Update(WindowId, CheckingExerciseType.PupilData,
            new ExerciseDatesItem { StartDate = new DateTime(2020, 1, 1), EndDate = DateTime.UtcNow.AddMonths(1) },
            CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
        await windowService.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExerciseDatesController Build(IWindowService windowService, HttpContext httpContext) =>
        new(windowService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

    private static CheckingWindowDraft Draft(params CheckingExerciseType[] exercises) => new()
    {
        Title = "Autumn 2026 checking window",
        CheckingWindowType = CheckingWindowType.KS4Autumn,
        KeyStage = KeyStages.KS4,
        Exercises = exercises
            .Select(e => new ExerciseDraft { ExerciseType = e, SortOrder = WindowExercises.SortOrderFor(e) })
            .ToList()
    };

    private static ISession SessionWithDraft(CheckingWindowDraft draft)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft));
        ISession session = Substitute.For<ISession>();
        session.TryGetValue("CheckingWindowDraft", out Arg.Any<byte[]>())
            .Returns(call =>
            {
                call[1] = bytes;
                return true;
            });
        return session;
    }

    private static CheckingWindowDraft SavedDraft(ISession session)
    {
        byte[] written = (byte[])session.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ISession.Set))
            .GetArguments()[1]!;
        return JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(written))!;
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "Existing window",
        StartDate = new DateTime(2027, 1, 1),
        EndDate = new DateTime(2027, 2, 1),
        KeyStage = KeyStages.KS2,
        CheckingWindowType = CheckingWindowType.KS2,
        Exercises =
        [
            new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 15, 17, 0, 0),
                SortOrder = 0
            }
        ]
    };
}
