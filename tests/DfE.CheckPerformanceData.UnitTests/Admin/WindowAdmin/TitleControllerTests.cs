using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class TitleControllerTests
{
    //New Path
    [Fact]
    public async Task Return_200_on_get_with_no_session_data()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        // A substituted session returns false from TryGetValue, so GetString/GetObject
        // return null - i.e. there is no CheckingWindowDraft for this window yet.
        var session = Substitute.For<ISession>();

        var controller = new TitleController(logger, windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal("New window", model.Title);
    }

    [Fact]
    public async Task Return_200_on_get_with_existing_session_title()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        var controller = new TitleController(logger, windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = SessionWithDraft(draft) }
            }
        };

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal("Autumn 2026 checking window", model.Title);
    }

    [Fact]
    public async Task Post_stores_submitted_title_in_session_and_redirects()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        // No existing draft in session, so the controller creates a new one.
        var session = Substitute.For<ISession>();
        byte[]? stored = null;
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        var controller = new TitleController(logger, windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };

        var model = new WindowTitleEditItem { Title = "Spring 2027 checking window" };

        var result = await controller.Index(model, CancellationToken.None);

        // The draft was written to the session with the submitted title.
        Assert.NotNull(stored);
        var savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal("Spring 2027 checking window", savedDraft!.Title);

        // With only a title set, the journey moves on to the start date step.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("New", redirect.ActionName);
        Assert.Equal("StartDate", redirect.ControllerName);
    }

    [Fact]
    public async Task Post_updates_title_on_existing_session_draft()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        // An existing draft already part-way through the journey (title + start date set).
        var existing = new CheckingWindowDraft
        {
            Title = "Old title",
            StartDate = new DateTime(2027, 1, 1)
        };

        byte[]? stored = null;
        var session = SessionWithDraft(existing);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        var controller = new TitleController(logger, windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };

        var model = new WindowTitleEditItem { Title = "Updated title" };

        var result = await controller.Index(model, CancellationToken.None);

        // The stored draft has the new title but keeps the other fields from the existing draft.
        Assert.NotNull(stored);
        var savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal("Updated title", savedDraft!.Title);
        Assert.Equal(new DateTime(2027, 1, 1), savedDraft.StartDate);

        // Title and start date are set, so the journey moves on to the end date step.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("New", redirect.ActionName);
        Assert.Equal("EndDate", redirect.ControllerName);
    }

    // Builds a substituted session that returns the given draft for the "CheckingWindowDraft"
    // key, serialized the same way SessionExtensions.SetObject stores it (JSON as UTF-8 bytes).
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
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Window(id, "Existing window title"));

        var controller = BuildController(logger, windowService);

        var result = await controller.Index(id, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTitleEditItem>(viewResult.Model);
        Assert.Equal(id, model.WindowId);
        Assert.Equal("Existing window title", model.Title);
    }

    [Fact]
    public async Task Edit_get_returns_not_found_when_window_does_not_exist()
    {
        var logger = Substitute.For<ILogger<TitleController>>();
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        // GetByIdAsync returns null (the default) when nothing is found for the id.
        var controller = BuildController(logger, windowService);

        var result = await controller.Index(id, CancellationToken.None);

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

        var result = await controller.Index(id, model, CancellationToken.None);

        // The service is asked to persist the window with the updated title.
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

        var result = await controller.Index(routeId, model, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        // Nothing should be loaded or saved when the ids do not match.
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    private static TitleController BuildController(ILogger<TitleController> logger, IWindowService windowService) =>
        new(logger, windowService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

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
