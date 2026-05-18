using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ContentBlockControllerTests
{
    private readonly IContentBlockService _service = Substitute.For<IContentBlockService>();
    private readonly ContentBlockController _sut;

    public ContentBlockControllerTests()
    {
        _sut = new ContentBlockController(_service);
    }

    // --- Save: open-redirect protection on ReturnUrl ---

    [Theory]
    [InlineData("https://evil.example.com/phish")]
    [InlineData("//evil.example.com/phish")]
    [InlineData("/\\evil.example.com")]
    [InlineData("javascript:alert(1)")]
    public async Task Save_WhenReturnUrlIsExternal_RedirectsToRoot(string externalUrl)
    {
        var model = new SaveContentBlockFormModel
        {
            Key = "k",
            BlockType = "Content",
            Value = "v",
            ReturnUrl = externalUrl
        };

        var result = await _sut.Save(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }

    [Fact]
    public async Task Save_WhenReturnUrlIsLocal_RedirectsToReturnUrl()
    {
        var model = new SaveContentBlockFormModel
        {
            Key = "k",
            BlockType = "Content",
            Value = "v",
            ReturnUrl = "/help/page"
        };

        var result = await _sut.Save(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/help/page", redirect.Url);
    }

    [Fact]
    public async Task Save_WhenModelInvalidAndReturnUrlExternal_RedirectsToRoot()
    {
        _sut.ModelState.AddModelError("Value", "required");
        var model = new SaveContentBlockFormModel
        {
            Key = "k",
            BlockType = "Content",
            ReturnUrl = "https://evil.example.com"
        };

        var result = await _sut.Save(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
        await _service.DidNotReceive().SaveAsync(Arg.Any<SaveContentBlockDto>());
    }

    // --- Revert: open-redirect protection on returnUrl ---

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("//evil.example.com")]
    public async Task Revert_WhenReturnUrlIsExternal_RedirectsToRoot(string externalUrl)
    {
        var result = await _sut.Revert("k", versionId: 1, returnUrl: externalUrl);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }

    [Fact]
    public async Task Revert_WhenReturnUrlIsLocal_RedirectsToReturnUrl()
    {
        var result = await _sut.Revert("k", versionId: 1, returnUrl: "/content-block/versions/k");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/content-block/versions/k", redirect.Url);
    }
}
