using System.Runtime.InteropServices.JavaScript;
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

public class StartDateControllerTests
{
    private IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public StartDateControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }
    
    [Fact]
    public async Task New_get_returns_bad_request_when_no_session_data()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        StartDateController controller = BuildController(windowService, new DefaultHttpContext { Session = Substitute.For<ISession>() });

        IActionResult result = await controller.New(CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public async Task New_get_returns_view_when_draft_exists()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        CheckingWindowDraft draft = new CheckingWindowDraft() { Title = "Autumn 2026 checking window" };
        StartDateController controller = BuildController(windowService, new DefaultHttpContext { Session = SessionWithDraft(draft) });
        controller.Url = _urlHelper;
        
        IActionResult result = await controller.New(CancellationToken.None);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        WindowDateEditItem model = Assert.IsType<WindowDateEditItem>(viewResult.Model);
        Assert.Equal(Guid.Empty, model.WindowId);
    }

    [Fact]
    public async Task New_post_returns_bad_request_when_no_session_data()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        StartDateController controller = BuildController(windowService, new DefaultHttpContext { Session = Substitute.For<ISession>() });

        WindowDateEditItem model = new WindowDateEditItem { DateValue = DateTime.UtcNow.AddMonths(1) };

        IActionResult result = await controller.Submit(model, CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No draft data", badRequest.Value);
    }

    [Fact]
    public async Task New_post_stores_start_date_in_session_and_redirects()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();
        var urlHelper = Substitute.For<IUrlHelper>();
        
        CheckingWindowDraft draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        byte[]? stored = null;
        ISession session = SessionWithDraft(draft);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>()))
            .Do(call => stored = (byte[])call[1]);

        StartDateController controller = BuildController(windowService, new DefaultHttpContext { Session = session });
        controller.Url = _urlHelper;

        DateTime startDate = DateTime.UtcNow.AddMonths(1);
        WindowDateEditItem model = new WindowDateEditItem { DateValue = startDate };

        IActionResult result = await controller.Submit(model, CancellationToken.None);

        Assert.NotNull(stored);
        CheckingWindowDraft? savedDraft = JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(stored!));
        Assert.NotNull(savedDraft);
        Assert.Equal(startDate, savedDraft!.StartDate);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task New_post_returns_view_and_does_not_save_when_date_is_in_the_past()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        CheckingWindowDraft draft = new CheckingWindowDraft { Title = "Autumn 2026 checking window" };

        bool saved = false;
        ISession session = SessionWithDraft(draft);
        session.When(s => s.Set("CheckingWindowDraft", Arg.Any<byte[]>())).Do(_ => saved = true);

        StartDateController controller = BuildController(windowService, new DefaultHttpContext { Session = session });

        WindowDateEditItem model = new WindowDateEditItem { DateValue = new DateTime(2020, 1, 1) };

        IActionResult result = await controller.Submit(model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
        Assert.False(saved, "The draft should not be saved when validation fails.");
    }

    [Fact]
    public async Task Edit_get_returns_view_with_the_windows_start_date()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();
        IUrlHelper? urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");

        Guid id = Guid.NewGuid();
        CheckingWindowDto window = Window(id);
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(window);

        StartDateController controller = BuildController(windowService, new DefaultHttpContext());
        controller.Url = urlHelper;

        IActionResult result = await controller.Edit(id, CancellationToken.None);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        WindowDateEditItem model = Assert.IsType<WindowDateEditItem>(viewResult.Model);
        Assert.Equal(id, model.WindowId);
        Assert.Equal(window.StartDate, model.DateValue);
    }

    [Fact]
    public async Task Edit_get_returns_not_found_when_window_does_not_exist()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        Guid id = Guid.NewGuid();
        StartDateController controller = BuildController(windowService, new DefaultHttpContext());

        IActionResult result = await controller.Edit(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_post_updates_the_window_and_redirects_to_summary()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        Guid id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        StartDateController controller = BuildController(windowService, new DefaultHttpContext());

        DateTime newStartDate = new DateTime(2027, 1, 15);
        WindowDateEditItem model = new WindowDateEditItem { WindowId = id, DateValue = newStartDate };

        IActionResult result = await controller.Update(id, model, CancellationToken.None);

        await windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.Id == id && w.StartDate == newStartDate),
            Arg.Any<CancellationToken>());

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
    }

    [Fact]
    public async Task Edit_post_returns_bad_request_when_route_id_does_not_match_model()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        Guid routeId = Guid.NewGuid();
        windowService.GetByIdAsync(routeId, Arg.Any<CancellationToken>()).Returns(Window(routeId));

        StartDateController controller = BuildController(windowService, new DefaultHttpContext());

        WindowDateEditItem model = new WindowDateEditItem { WindowId = Guid.NewGuid(), DateValue = new DateTime(2027, 1, 15) };

        IActionResult result = await controller.Update(routeId, model, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_post_returns_view_and_does_not_save_when_date_is_in_the_past()
    {
        IWindowService? windowService = Substitute.For<IWindowService>();

        Guid id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        StartDateController controller = BuildController(windowService, new DefaultHttpContext());

        WindowDateEditItem model = new WindowDateEditItem { WindowId = id, DateValue = new DateTime(2020, 1, 1) };

        IActionResult result = await controller.Update(id, model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    private static StartDateController BuildController(IWindowService windowService, HttpContext httpContext) =>
        new(Substitute.For<ILogger<StartDateController>>(), windowService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

     private static ISession SessionWithDraft(CheckingWindowDraft draft)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft));
        ISession? session = Substitute.For<ISession>();
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
