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

// The create wizard has no exercise step. The window type gives the new window its default
// exercises, and the admin adds others from the window's summary page after it is created.
public class WindowTypeControllerTests
{
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public WindowTypeControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>())
            .Returns(call => call.Arg<UrlActionContext>().Controller);
    }

    [Fact]
    public async Task Choosing_a_type_takes_its_default_exercises_and_goes_to_the_first_date_page()
    {
        ISession session = SessionWithDraft(new CheckingWindowDraft { Title = "A window" });
        WindowTypeController controller = Build(session);

        IActionResult result = await controller.Submit(
            new WindowTypeItem { WindowType = CheckingWindowType.Post16 }, CancellationToken.None);

        Assert.Equal("ExerciseDates", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            SavedDraft(session).Exercises.Select(e => e.ExerciseType));
    }

    [Fact]
    public async Task Changing_the_type_replaces_the_exercises_with_the_new_types_defaults()
    {
        CheckingWindowDraft draft = new()
        {
            Title = "A window",
            CheckingWindowType = CheckingWindowType.Post16,
            Exercises =
            [
                new ExerciseDraft { ExerciseType = CheckingExerciseType.PupilData, TabOrder = 100 },
                new ExerciseDraft { ExerciseType = CheckingExerciseType.ResultsEnquiry, TabOrder = 200 }
            ]
        };
        ISession session = SessionWithDraft(draft);

        await Build(session).Submit(
            new WindowTypeItem { WindowType = CheckingWindowType.KS4June }, CancellationToken.None);

        Assert.Equal(CheckingExerciseType.PupilData, Assert.Single(SavedDraft(session).Exercises).ExerciseType);
    }

    private WindowTypeController Build(ISession session) =>
        new(Substitute.For<IWindowService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            },
            Url = _urlHelper
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
