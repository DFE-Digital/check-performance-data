using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class StartDateControllerTests
{
    //New path
    [Fact]
    public async Task New_get_returns_bad_request_when_no_session_data()
    {
        var windowService = Substitute.For<IWindowService>();

        // A substituted session returns false from TryGetValue, so there is no draft.
        var controller = BuildController(windowService, new DefaultHttpContext { Session = Substitute.For<ISession>() });

        var result = await controller.Index(CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public async Task New_get_returns_view_when_draft_exists()
    {
        var windowService = Substitute.For<IWindowService>();

        var draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };
        var controller = BuildController(windowService, new DefaultHttpContext { Session = SessionWithDraft(draft) });

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowDateEditItem>(viewResult.Model);
        Assert.Equal(Guid.Empty, model.WindowId);
        Assert.Equal("/admin/windows/start-date", model.PostUrl);
        Assert.True(model.DateValue > DateTime.UtcNow, "The default date should be in the future.");
    }

    [Fact]
    public async Task New_post_returns_bad_request_when_no_session_data()
    {
        var windowService = Substitute.For<IWindowService>();

        var controller = BuildController(windowService, new DefaultHttpContext { Session = Substitute.For<ISession>() });

        var model = new WindowDateEditItem { DateValue = DateTime.UtcNow.AddMonths(1) };

        var result = await controller.Index(model, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public async Task New_post_stores_start_date_in_session_and_redirects()
    {
        var windowService = Substitute.For<IWindowService>();

        // An in-journey draft that already has its title.
        var draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        byte[]? stored = null;
        var session = SessionWithDraft(draft);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        var controller = BuildController(windowService, new DefaultHttpContext { Session = session });

        var startDate = DateTime.UtcNow.AddMonths(1);
        var model = new WindowDateEditItem { DateValue = startDate };

        var result = await controller.Index(model, CancellationToken.None);

        // The draft was written back with the submitted start date.
        Assert.NotNull(stored);
        var savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal(startDate, savedDraft!.StartDate);

        // Title and start date are set, so the journey moves on to the end date step.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("New", redirect.ActionName);
        Assert.Equal("EndDate", redirect.ControllerName);
    }

    [Fact]
    public async Task New_post_returns_view_and_does_not_save_when_date_is_in_the_past()
    {
        var windowService = Substitute.For<IWindowService>();

        var draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        var saved = false;
        var session = SessionWithDraft(draft);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>())).Do(_ => saved = true);

        var controller = BuildController(windowService, new DefaultHttpContext { Session = session });

        var model = new WindowDateEditItem { DateValue = new DateTime(2020, 1, 1) };

        var result = await controller.Index(model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
        Assert.False(saved, "The draft should not be saved when validation fails.");
    }

    //Edit path
    [Fact]
    public async Task Edit_get_returns_view_with_the_windows_start_date()
    {
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        var window = Window(id);
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(window);

        var controller = BuildController(windowService, new DefaultHttpContext());

        var result = await controller.Index(id, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowDateEditItem>(viewResult.Model);
        Assert.Equal(id, model.WindowId);
        Assert.Equal(window.StartDate, model.DateValue);
        Assert.Equal($"/admin/windows/{id}/start-date", model.PostUrl);
    }

    [Fact]
    public async Task Edit_get_returns_not_found_when_window_does_not_exist()
    {
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        // GetByIdAsync returns null (the default) when nothing is found for the id.
        var controller = BuildController(windowService, new DefaultHttpContext());

        var result = await controller.Index(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_post_updates_the_window_and_redirects_to_summary()
    {
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        var controller = BuildController(windowService, new DefaultHttpContext());

        var newStartDate = new DateTime(2027, 1, 15);
        var model = new WindowDateEditItem { WindowId = id, DateValue = newStartDate };

        var result = await controller.Index(id, model, CancellationToken.None);

        await windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.Id == id && w.StartDate == newStartDate),
            Arg.Any<CancellationToken>());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
    }

    [Fact]
    public async Task Edit_post_returns_bad_request_when_route_id_does_not_match_model()
    {
        var windowService = Substitute.For<IWindowService>();

        var routeId = Guid.NewGuid();
        // The window must load so validation passes and we reach the id-mismatch check.
        windowService.GetByIdAsync(routeId, Arg.Any<CancellationToken>()).Returns(Window(routeId));

        var controller = BuildController(windowService, new DefaultHttpContext());

        var model = new WindowDateEditItem { WindowId = Guid.NewGuid(), DateValue = new DateTime(2027, 1, 15) };

        var result = await controller.Index(routeId, model, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_post_returns_view_and_does_not_save_when_date_is_in_the_past()
    {
        var windowService = Substitute.For<IWindowService>();

        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        var controller = BuildController(windowService, new DefaultHttpContext());

        var model = new WindowDateEditItem { WindowId = id, DateValue = new DateTime(2020, 1, 1) };

        var result = await controller.Index(id, model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    private static StartDateController BuildController(IWindowService windowService, HttpContext httpContext) =>
        new(Substitute.For<ILogger<StartDateController>>(), windowService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

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

    private static CheckingWindowDto Window(Guid id) =>
        new()
        {
            Id = id,
            Title = "Existing window",
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 2, 1),
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };
}
