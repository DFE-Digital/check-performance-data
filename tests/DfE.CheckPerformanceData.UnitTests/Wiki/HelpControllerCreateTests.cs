using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class HelpControllerCreateTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    private readonly HelpController _sut;
    private readonly TempDataDictionary _tempData;

    public HelpControllerCreateTests()
    {
        _sut = new HelpController(_wikiService);

        var httpContext = new DefaultHttpContext();
        _tempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
        _sut.TempData = _tempData;
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task Create_DuplicateTitle_RedirectsToHelp_WithErrorAndPreservedTitle()
    {
        var model = new CreateWikiPageViewModel { Title = "Existing", Content = "x", ParentId = 4 };
        _wikiService.CreatePageAsync(Arg.Any<CreateWikiPageDto>())
            .Throws(new DuplicateWikiPageException("Existing", 4));

        var result = await _sut.Create(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/help", redirect.Url);

        Assert.True(_tempData.ContainsKey("WikiCreateError"));
        Assert.Contains("already exists", (string)_tempData["WikiCreateError"]!);
        Assert.Equal("Existing", _tempData["WikiCreateAttemptedTitle"]);
        Assert.Equal(4, _tempData["WikiCreateAttemptedParentId"]);

        await _wikiService.Received(1).CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    [Fact]
    public async Task Create_DuplicateTitle_InEditMode_RedirectsBackToEditMode()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?edit");
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
        _sut.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());

        var model = new CreateWikiPageViewModel { Title = "Existing", Content = "x", ParentId = null };
        _wikiService.CreatePageAsync(Arg.Any<CreateWikiPageDto>())
            .Throws(new DuplicateWikiPageException("Existing", null));

        var result = await _sut.Create(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/help?edit", redirect.Url);
    }

    [Fact]
    public async Task Create_HappyPath_DoesNotSetTempDataError()
    {
        var model = new CreateWikiPageViewModel { Title = "Brand new", Content = "x", ParentId = null };
        _wikiService.CreatePageAsync(Arg.Any<CreateWikiPageDto>())
            .Returns(new WikiPageDto { Id = 1, Title = "Brand new", Slug = "brand-new", SlugPath = "brand-new" });

        var result = await _sut.Create(model);

        Assert.IsType<RedirectResult>(result);
        Assert.False(_tempData.ContainsKey("WikiCreateError"));
    }
}
