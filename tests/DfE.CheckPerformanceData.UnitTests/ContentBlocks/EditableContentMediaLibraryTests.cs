using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.ViewComponents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

// The block editor on a page offers the media library only to people with the media section,
// because the picker and its upload are gated on that grant. The check runs only when the editor
// is open.
public sealed class EditableContentMediaLibraryTests
{
    private readonly IContentBlockService _blocks = Substitute.For<IContentBlockService>();
    private readonly IAdminAccessPolicy _access = Substitute.For<IAdminAccessPolicy>();

    private async Task<EditableContentViewModel> InvokeAsync(string query)
    {
        _blocks.EnsureAsync("intro", "Content", Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ContentBlockDto { Key = "intro", BlockType = "Content", Value = "<p>x</p>", ValueHtml = "<p>x</p>" });
        var http = new DefaultHttpContext();
        http.Request.Path = "/somewhere";
        http.Request.QueryString = new QueryString(query);
        var component = new EditableContentViewComponent(_blocks, accessPolicy: _access)
        {
            ViewComponentContext = new ViewComponentContext { ViewContext = new ViewContext { HttpContext = http } }
        };

        var result = Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync("intro", "<p>x</p>"));
        return Assert.IsType<EditableContentViewModel>(result.ViewData!.Model);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheOpenEditor_OffersTheLibrary_OnlyToPeopleWithTheMediaSection(bool hasMedia)
    {
        _access.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), "media").Returns(hasMedia);
        _access.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), "content-pages").Returns(true);

        var model = await InvokeAsync("?edit=intro");

        Assert.True(model.IsEditing);
        Assert.Equal(hasMedia, model.CanUseMediaLibrary);
    }

    [Fact]
    public async Task AClosedBlock_DoesNotAsk()
    {
        var model = await InvokeAsync("");

        Assert.False(model.CanUseMediaLibrary);
        await _access.DidNotReceive().CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>());
    }
}
