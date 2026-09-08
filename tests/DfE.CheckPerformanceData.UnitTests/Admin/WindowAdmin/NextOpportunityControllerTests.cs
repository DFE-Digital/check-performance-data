using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#298317: the per-window "next opportunity" date, edited from the Summary exactly as the
// turnaround commitment is. Optional — an admin may not know the date yet, and may clear it. The
// GOV.UK date-input binder only raises its own "must be a real date" error for a *required* date
// field, so an impossible date on this optional one silently binds to null — indistinguishable
// from a deliberate clear — unless the controller checks the raw posted date parts itself.
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
        // Empty form by default: the invalid-date check reads Request.Form, so every test needs a
        // real HttpContext even when it never posts a date part.
        _sut = new NextOpportunityController(_windowService)
        {
            Url = _urlHelper,
            ControllerContext = new ControllerContext { HttpContext = ContextWithForm() }
        };
    }

    private static DefaultHttpContext ContextWithForm(params (string Key, string Value)[] fields)
    {
        var context = new DefaultHttpContext();
        context.Request.Form = new FormCollection(
            fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));
        return context;
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
    public async Task Update_redisplays_the_page_with_its_urls_when_something_else_already_invalidated_it()
    {
        // A pre-existing ModelState error (from any source) short-circuits the controller's own
        // invalid-date check, so it redisplays without touching Request.Form at all.
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

    [Fact]
    public async Task An_impossible_date_the_binder_silently_nulled_is_rejected_not_saved_as_cleared()
    {
        // The GOV.UK date-input binder does not raise its own error for this optional field — it
        // just binds an impossible date (e.g. 31 February) to null, same as a genuinely blank
        // submission. The controller tells the two apart from the raw posted date parts.
        // The invalid-date check runs after the window loads (F8), so a window must be stubbed.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(October2027));
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = ContextWithForm(
                ("NextOpportunity.Day", "31"), ("NextOpportunity.Month", "2"), ("NextOpportunity.Year", "2027"))
        };

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = null },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(PageView(), view.ViewName);
        // Pins the ModelState entry the controller adds, not the text an admin actually sees — the
        // GOV.UK date-input tag helper composes its own rendered message from the field's
        // [Display(Name = ...)] (review F3), not from this string.
        Assert.Equal(
            "Next opportunity must be a real date",
            _sut.ModelState[nameof(WindowNextOpportunityEditItem.NextOpportunity)]!.Errors[0].ErrorMessage);
        await _windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_genuinely_blank_submission_is_still_accepted_as_a_clear()
    {
        // The empty-form default from the constructor: WasDatePosted() is false, so the same
        // null-binding result that the impossible-date test rejects is accepted here.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(October2027));

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = null },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        await _windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.NextOpportunity == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_form_post_is_treated_as_left_blank_not_a_500()
    {
        // AB#298317 review F1: WasDatePosted() read Request.Form unguarded. An authenticated admin
        // can reach Update with a non-form body (e.g. JSON) — [ValidateAntiForgeryToken] accepts the
        // X-XSRF-TOKEN header as well as the form field — and Request.Form throws for a request with
        // no form content type. A non-form POST carries no date parts to read, so it must be treated
        // the same as "left blank".
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(October2027));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _sut.Update(WindowId,
            new WindowNextOpportunityEditItem { WindowId = WindowId, NextOpportunity = null },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
        await _windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.NextOpportunity == null),
            Arg.Any<CancellationToken>());
    }

    private static string PageView() => "~/Views/WindowAdmin/NextOpportunity.cshtml";
}
