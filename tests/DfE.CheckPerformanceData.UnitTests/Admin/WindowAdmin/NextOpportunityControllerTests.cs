using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#298317: the per-window "next opportunity" date, edited from the Summary exactly as the
// turnaround commitment is. Optional — an admin may not know the date yet, and may clear it.
public sealed class NextOpportunityControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime October2027 = new(2027, 10, 1);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();
    private readonly NextOpportunityController _sut;

    public NextOpportunityControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
        _sut = new NextOpportunityController(_windowService) { Url = _urlHelper };
    }

    private static CheckingWindowDto Window(DateTime? nextOpportunity) => new()
    {
        Id = WindowId,
        Title = "16 to 19",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 5),
        EndDate = new DateTime(2027, 3, 31),
        NextOpportunity = nextOpportunity
    };

    [Fact]
    public async Task Edit_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        Assert.IsType<NotFoundResult>(await _sut.Edit(WindowId, CancellationToken.None));
    }

    [Fact]
    public async Task Edit_prefills_the_current_date()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(October2027));

        var view = Assert.IsType<ViewResult>(await _sut.Edit(WindowId, CancellationToken.None));
        var model = Assert.IsType<WindowNextOpportunityEditItem>(view.Model);

        Assert.Equal(WindowId, model.WindowId);
        Assert.Equal(October2027, model.NextOpportunity);
        Assert.Equal("/dummy-url", model.PostUrl);
        Assert.Equal("/dummy-url", model.CancelUrl);
    }

    [Fact]
    public async Task Update_saves_the_date_at_midnight_and_returns_to_the_summary()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(null));

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = new DateTime(2027, 10, 14, 9, 30, 0) },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
        await _windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.NextOpportunity == new DateTime(2027, 10, 14)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_with_no_date_clears_it()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(October2027));

        await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = null },
            CancellationToken.None);

        await _windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.NextOpportunity == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_rejects_a_posted_id_that_disagrees_with_the_route()
    {
        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = Guid.NewGuid(), NextOpportunity = October2027 },
            CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await _windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_returns_not_found_when_the_window_has_gone()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = October2027 },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_redisplays_the_page_with_its_urls_when_the_date_is_invalid()
    {
        // The GOV.UK date-input binder adds the model error; the controller only has to redisplay
        // and re-decorate the urls, which are not posted back.
        _sut.ModelState.AddModelError(nameof(WindowNextOpportunityEditItem.NextOpportunity), "Next opportunity must be a real date");

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowNextOpportunityEditItem>(view.Model);
        Assert.Equal("/dummy-url", model.PostUrl);
        Assert.Equal("/dummy-url", model.CancelUrl);
        await _windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }
}
