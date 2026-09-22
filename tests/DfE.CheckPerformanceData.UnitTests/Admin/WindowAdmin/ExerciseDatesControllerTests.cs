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

// #319, #466: replaces StartDateControllerTests and EndDateControllerTests. There is no
// window-level date step any more — an exercise's dates are captured on one page, keyed by its
// position in the draft's exercise list (a display-only exercise has no kind to key on), and the
// window's own pair is derived from them as their union, so the two can never disagree. Editing an
// existing window's exercise dates is a separate page (Task 8).
public class ExerciseDatesControllerTests
{
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ExerciseDatesControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    // ── New (draft) ──────────────────────────────────────────────────────────

    [Fact]
    public void New_get_returns_bad_request_when_no_session_data()
    {
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = Substitute.For<ISession>() });

        IActionResult result = controller.New(0);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public void New_get_returns_not_found_for_an_exercise_the_draft_does_not_run()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = SessionWithDraft(draft) });

        Assert.IsType<NotFoundResult>(controller.New(5));
    }

    [Fact]
    public void New_get_returns_the_view_labelled_with_the_exercise()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.ResultsEnquiry);
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(controller.New(0));
        ExerciseDatesItem model = Assert.IsType<ExerciseDatesItem>(view.Model);

        Assert.Equal(0, model.Index);
        Assert.Equal("Results enquiry", model.ExerciseLabel);
    }

    [Fact]
    public void New_get_defaults_a_new_exercise_to_midnight_and_five_pm()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(controller.New(0));
        ExerciseDatesItem model = Assert.IsType<ExerciseDatesItem>(view.Model);

        Assert.Equal(0, model.StartHour);
        Assert.Equal(17, model.EndHour);
    }

    [Fact]
    public void New_post_stores_both_dates_on_the_exercise_and_redirects()
    {
        CheckingWindowDraft draft = Draft(CheckingExerciseType.PupilData);
        ISession session = SessionWithDraft(draft);
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = session });
        controller.Url = _urlHelper;

        DateTime start = DateTime.UtcNow.AddMonths(1).Date;
        DateTime end = DateTime.UtcNow.AddMonths(2).Date;

        IActionResult result = controller.Submit(0, new ExerciseDatesItem
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
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        IActionResult result = controller.Submit(0, new ExerciseDatesItem
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
        ExerciseDatesController controller = Build(new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;

        IActionResult result = controller.Submit(0, new ExerciseDatesItem
        {
            StartDate = DateTime.UtcNow.AddMonths(2).Date,
            EndDate = DateTime.UtcNow.AddMonths(1).Date
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExerciseDatesController Build(HttpContext httpContext) =>
        new()
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

    private static CheckingWindowDraft Draft(params CheckingExerciseType[] exercises) => new()
    {
        Title = "Autumn 2026 checking window",
        CheckingWindowType = CheckingWindowType.KS4Autumn,
        KeyStage = KeyStages.KS4,
        Exercises = exercises
            .Select(e => new ExerciseDraft
            {
                ExerciseType = e,
                Name = CheckingExerciseNames.NameFor(e),
                SortOrder = WindowExercises.SortOrderFor(e)
            })
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
}
