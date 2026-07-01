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

    // --- Index: content-blocks management page ---

    [Fact]
    public async Task Index_ReturnsAllBlocks_FromService()
    {
        var blocks = new List<ContentBlockDto>
        {
            new() { Id = 1, Key = "home-content", BlockType = "Content", Value = "a" },
            new() { Id = 2, Key = "guidance-landing-intro", BlockType = "Content", Value = "b" }
        };
        _service.GetAllAsync().Returns(blocks);

        var result = await _sut.Index(edit: null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ContentBlocksAdminViewModel>(view.Model);
        Assert.Equal(blocks, model.Blocks);
        Assert.Null(model.EditKey);
    }

    [Fact]
    public async Task Index_PassesEditKeyThrough_SoTheViewOpensThatEditor()
    {
        _service.GetAllAsync().Returns([]);

        var result = await _sut.Index(edit: "home-content");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ContentBlocksAdminViewModel>(view.Model);
        Assert.Equal("home-content", model.EditKey);
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

    // --- Save: scroll-restore via Anchor fragment ---

    [Fact]
    public async Task Save_WhenAnchorProvided_AppendsFragmentToRedirect()
    {
        var model = new SaveContentBlockFormModel
        {
            Key = "k",
            BlockType = "Content",
            Value = "v",
            ReturnUrl = "/guidance/2026-ks4-june-checking-exercise?edit=k",
            Anchor = "block-k"
        };

        var result = await _sut.Save(model);

        // edit param stripped, the editing location preserved as a fragment so the
        // post-save redirect lands the editor back where they were working.
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/guidance/2026-ks4-june-checking-exercise#block-k", redirect.Url);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("foo bar")]
    [InlineData("a/b")]
    public async Task Save_WhenAnchorUnsafe_OmitsFragment(string anchor)
    {
        var model = new SaveContentBlockFormModel
        {
            Key = "k",
            BlockType = "Content",
            Value = "v",
            ReturnUrl = "/guidance/x",
            Anchor = anchor
        };

        var result = await _sut.Save(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/guidance/x", redirect.Url);
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
