using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class TitleControllerTests
{
    IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public TitleControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }
    
    [Fact]
    public async Task Return_200_on_get_with_no_session_data()
    {
        var windowService = Substitute.For<IWindowService>();

        var session = Substitute.For<ISession>();

        var controller = new TitleController(windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            },
            Url = StubUrlHelper()
        };

        var result = await controller.New(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal("New window", model.Title);
    }

    [Fact]
    public async Task Return_200_on_get_with_existing_session_title()
    {
        var windowService = Substitute.For<IWindowService>();

        var draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        var controller = new TitleController(windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = SessionWithDraft(draft) }
            },
            Url = StubUrlHelper()
        };

        var result = await controller.New(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal("Autumn 2026 checking window", model.Title);
    }

    [Fact]
    public async Task Post_stores_submitted_title_in_session_and_redirects()
    {
        var windowService = Substitute.For<IWindowService>();

        var session = Substitute.For<ISession>();
        byte[]? stored = null;
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        var controller = new TitleController(windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            },
            Url = _urlHelper
        };

        var model = new WindowTitleEditItem { Title = "Spring 2027 checking window" };

        var result = await controller.Submit(model, CancellationToken.None);

        Assert.NotNull(stored);
        var savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal("Spring 2027 checking window", savedDraft!.Title);
        Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task Post_updates_title_on_existing_session_draft()
    {
        var windowService = Substitute.For<IWindowService>();

        // #319: the draft has no window-level dates any more — they are derived from its exercises,
        // so the "other answers survive a title edit" assertion below reads an exercise instead.
        var existing = new CheckingWindowDraft
        {
            Title = "Old title",
            Exercises =
            [
                new ExerciseDraft
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2027, 1, 1),
                    EndDate = new DateTime(2027, 1, 14)
                }
            ]
        };

        byte[]? stored = null;
        var session = SessionWithDraft(existing);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        var controller = new TitleController(windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            },
            Url = _urlHelper
        };

        var model = new WindowTitleEditItem { Title = "Updated title" };
        var result = await controller.Submit(model, CancellationToken.None);

        Assert.NotNull(stored);
        var savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal("Updated title", savedDraft!.Title);
        Assert.Equal(new DateTime(2027, 1, 1), Assert.Single(savedDraft.Exercises).StartDate);

        Assert.IsType<RedirectResult>(result);
    }

    private static ISession SessionWithDraft(CheckingWindowDraft draft)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft));
        var session = Substitute.For<ISession>();
        session.TryGetValue("CheckingWindowDraft", out Arg.Any<byte[]>())
            .Returns(call =>
            {
                call[1] = bytes;
                return true;
            });
        return session;
    }

    //Edit path
    [Fact]
    public async Task Edit_get_returns_view_with_the_windows_title()
    {
        ILogger<TitleController>? logger = Substitute.For<ILogger<TitleController>>();
        IWindowService? windowService = Substitute.For<IWindowService>();

        Guid id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Window(id, "Existing window title"));

        TitleController controller = BuildController(logger, windowService);

        IActionResult result = await controller.Edit(id, CancellationToken.None);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        WindowTitleEditItem model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal(id, model.WindowId);
        Assert.Equal("Existing window title", model.Title);
    }

    [Fact]
    public async Task Edit_get_returns_not_found_when_window_does_not_exist()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        var controller = BuildController(logger, windowService);

        var result = await controller.Edit(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_post_updates_the_window_and_redirects_to_summary()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Window(id, "Old title"));

        var controller = BuildController(logger, windowService);

        var model = new WindowTitleEditItem { WindowId = id, Title = "New edited title" };

        var result = await controller.Update(id, model, CancellationToken.None);

        await windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.Id == id && w.Title == "New edited title"),
            Arg.Any<CancellationToken>());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
    }

    [Fact]
    public async Task Edit_post_returns_bad_request_when_route_id_does_not_match_model()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var controller = BuildController(logger, windowService);

        var routeId = Guid.NewGuid();
        var model = new WindowTitleEditItem { WindowId = Guid.NewGuid(), Title = "Mismatched" };

        var result = await controller.Update(routeId, model, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    private static TitleController BuildController(ILogger<TitleController> logger, IWindowService windowService) =>
        new(windowService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = StubUrlHelper()
        };

    private static IUrlHelper StubUrlHelper()
    {
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.Action(Arg.Any<UrlActionContext>()).Returns(ci => "/");
        return urlHelper;
    }

    private static CheckingWindowDto Window(Guid id, string title) =>
        new()
        {
            Id = id,
            Title = title,
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 2, 1),
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };
}
