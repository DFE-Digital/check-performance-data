using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;
using System.Security.Claims;

namespace DfE.CheckPerformanceData.UnitTests.SiteAssets;

public sealed class AdminSiteAssetsControllerTests
{
    private readonly ISiteAssetService _assets = Substitute.For<ISiteAssetService>();
    private readonly AdminSiteAssetsController _sut;

    public AdminSiteAssetsControllerTests()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin@example.test")], "test"))
        };
        _sut = new AdminSiteAssetsController(_assets, NullLogger<AdminSiteAssetsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>())
        };
    }

    [Fact]
    public void Controller_IsGatedByTheSiteAssetsSection_AndByTheAdministratorRole()
    {
        var type = typeof(AdminSiteAssetsController);

        Assert.Equal(AdminNavKeys.SiteAssets, type.GetCustomAttribute<RequireAdminSectionAttribute>()!.SectionKey);
        Assert.Equal("cypmd_admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public void Save_RequiresAnAntiForgeryToken()
    {
        var save = typeof(AdminSiteAssetsController).GetMethod(nameof(AdminSiteAssetsController.Save))!;

        Assert.NotNull(save.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(save.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact]
    public async Task Index_ShowsTheStoredAssets()
    {
        _assets.GetAsync().Returns(new SiteAssetContent("a{}", "b()", true, false));

        var view = Assert.IsType<ViewResult>(await _sut.Index());
        var model = Assert.IsType<AdminSiteAssetsViewModel>(view.Model);

        Assert.Equal("a{}", model.Css);
        Assert.Equal("b()", model.Js);
        Assert.True(model.CssEnabled);
        Assert.False(model.JsEnabled);
    }

    [Fact]
    public async Task Save_StoresTheSubmittedValues_AndRedirectsWithAConfirmation()
    {
        _assets.SaveAsync(Arg.Any<SiteAssetContent>()).Returns(new SiteAssetSaveResult(true));

        var result = await _sut.Save(new AdminSiteAssetsViewModel { Css = "a{}", Js = "b()", CssEnabled = true, JsEnabled = false });

        Assert.IsType<RedirectResult>(result);
        await _assets.Received(1).SaveAsync(new SiteAssetContent("a{}", "b()", true, false));
        Assert.NotNull(_sut.TempData["SiteAssetsResult"]);
    }

    [Fact]
    public async Task Save_NullText_IsTreatedAsClearing()
    {
        _assets.SaveAsync(Arg.Any<SiteAssetContent>()).Returns(new SiteAssetSaveResult(true));

        await _sut.Save(new AdminSiteAssetsViewModel { Css = null, Js = null });

        await _assets.Received(1).SaveAsync(new SiteAssetContent("", "", false, false));
    }

    [Fact]
    public async Task Save_WhenRejected_RedisplaysWhatWasTypedWithTheError()
    {
        _assets.SaveAsync(Arg.Any<SiteAssetContent>()).Returns(new SiteAssetSaveResult(false, "The CSS is too long."));

        var result = await _sut.Save(new AdminSiteAssetsViewModel { Css = "keep me", CssEnabled = true });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminSiteAssetsViewModel>(view.Model);
        Assert.Equal("keep me", model.Css);
        Assert.Equal("The CSS is too long.", model.Error);
    }
}
