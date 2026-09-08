using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#298317 review: NextOpportunityController was written as a copy of this one and fixed a
// redisplay bug in its copy — a validation failure returned the posted model, whose PostUrl and
// CancelUrl are never posted back, so the redisplayed page's form action and Cancel link were
// empty. That fix is back-ported here and pinned.
public sealed class TurnaroundCommitmentControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();
    private readonly TurnaroundCommitmentController _sut;

    public TurnaroundCommitmentControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
        _sut = new TurnaroundCommitmentController(_windowService) { Url = _urlHelper };
    }

    private static CheckingWindowDto Window(string commitment) => new()
    {
        Id = WindowId,
        Title = "16 to 19",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 5),
        EndDate = new DateTime(2027, 3, 31),
        TurnaroundCommitment = commitment
    };

    [Fact]
    public async Task Edit_prefills_the_current_commitment_and_its_urls()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window("updated in the Spring"));

        var result = await _sut.Edit(WindowId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTurnaroundCommitmentEditItem>(view.Model);
        Assert.Equal(WindowId, model.WindowId);
        Assert.Equal("updated in the Spring", model.TurnaroundCommitment);
        Assert.Equal("/dummy-url", model.PostUrl);
        Assert.Equal("/dummy-url", model.CancelUrl);
    }

    [Fact]
    public async Task Update_redisplays_the_page_with_its_urls_when_the_commitment_is_invalid()
    {
        // [MaxLength(200)] is what fails in practice; the source of the error does not matter.
        _sut.ModelState.AddModelError(nameof(WindowTurnaroundCommitmentEditItem.TurnaroundCommitment),
            "Turnaround commitment must be 200 characters or less");

        var result = await _sut.Update(WindowId,
            new WindowTurnaroundCommitmentEditItem { WindowId = WindowId, TurnaroundCommitment = new string('x', 201) },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WindowTurnaroundCommitmentEditItem>(view.Model);
        Assert.Equal("/dummy-url", model.PostUrl);
        Assert.Equal("/dummy-url", model.CancelUrl);
        await _windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_saves_the_commitment_and_returns_to_the_summary()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window("old"));

        var result = await _sut.Update(WindowId,
            new WindowTurnaroundCommitmentEditItem { WindowId = WindowId, TurnaroundCommitment = "new" },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Summary", redirect.ControllerName);
        await _windowService.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.TurnaroundCommitment == "new"), Arg.Any<CancellationToken>());
    }
}
