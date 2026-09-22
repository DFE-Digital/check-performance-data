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

// #466: the window type decides which exercise templates are on offer, so changing it after
// exercises were already ticked must not leave the old type's templates — including a display-only
// one no longer offered at all — stuck in the draft.
public class WindowTypeControllerTests
{
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public WindowTypeControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    [Fact]
    public async Task Submit_clears_the_drafts_exercises_when_the_window_type_changes()
    {
        CheckingWindowDraft draft = new()
        {
            Title = "A window",
            CheckingWindowType = CheckingWindowType.KS4June,
            KeyStage = KeyStages.KS4,
            Exercises = [new ExerciseDraft { ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking" }]
        };
        ISession session = SessionWithDraft(draft);
        WindowTypeController controller = Build(session);

        await controller.Submit(new WindowTypeItem { WindowType = CheckingWindowType.Post16 }, CancellationToken.None);

        Assert.Empty(SavedDraft(session).Exercises);
    }

    [Fact]
    public async Task Submit_keeps_the_drafts_exercises_when_the_window_type_is_unchanged()
    {
        CheckingWindowDraft draft = new()
        {
            Title = "A window",
            CheckingWindowType = CheckingWindowType.KS4June,
            KeyStage = KeyStages.KS4,
            Exercises = [new ExerciseDraft { ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking" }]
        };
        ISession session = SessionWithDraft(draft);
        WindowTypeController controller = Build(session);

        await controller.Submit(new WindowTypeItem { WindowType = CheckingWindowType.KS4June }, CancellationToken.None);

        Assert.Single(SavedDraft(session).Exercises);
    }

    private WindowTypeController Build(ISession session)
    {
        WindowTypeController controller = new(Substitute.For<IWindowService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };
        controller.Url = _urlHelper;
        return controller;
    }

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
