using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Web.ViewComponents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

public sealed class ContentTextViewComponentTests
{
    private const string Key = "a-block";
    private const string Default = "The default words";
    private const string PagePath = "/a/page";
    private const string PlainTextBlockType = "Title";

    private readonly IContentBlockService _blocks = Substitute.For<IContentBlockService>();
    private readonly IEstablishmentViewContext _viewContext = Substitute.For<IEstablishmentViewContext>();

    private async Task<string> RenderAsync()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = PagePath;
        var writer = new StringWriter();
        var context = new ViewComponentContext
        {
            ViewContext = new ViewContext { HttpContext = http, Writer = writer },
            HtmlEncoder = HtmlEncoder.Default
        };
        var component = new ContentTextViewComponent(_blocks, _viewContext) { ViewComponentContext = context };

        var result = await component.InvokeAsync(Key, Default);
        result.Execute(context);
        return writer.ToString();
    }

    private void TheBlockHolds(string value) =>
        _blocks.EnsureAsync(Key, PlainTextBlockType, Default, PagePath)
            .Returns(new ContentBlockDto { Key = Key, BlockType = PlainTextBlockType, Value = value });

    [Fact]
    public async Task It_shows_the_text_of_the_block()
    {
        TheBlockHolds("The edited words");

        Assert.Equal("The edited words", await RenderAsync());
    }

    [Fact]
    public async Task It_makes_the_block_as_plain_text_on_the_page_that_shows_it()
    {
        TheBlockHolds(Default);

        await RenderAsync();

        await _blocks.Received(1).EnsureAsync(Key, PlainTextBlockType, Default, PagePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_block_with_no_text_shows_the_default(string value)
    {
        TheBlockHolds(value);

        Assert.Equal(Default, await RenderAsync());
    }

    [Fact]
    public async Task It_encodes_the_text()
    {
        TheBlockHolds("<b>Fish & chips</b>");

        Assert.Equal("&lt;b&gt;Fish &amp; chips&lt;/b&gt;", await RenderAsync());
    }

    [Fact]
    public async Task An_admin_who_views_as_a_school_reads_the_block_and_does_not_make_it()
    {
        _viewContext.IsImpersonating.Returns(true);
        _blocks.GetByKeyAsync(Key)
            .Returns(new ContentBlockDto { Key = Key, BlockType = PlainTextBlockType, Value = "The edited words" });

        Assert.Equal("The edited words", await RenderAsync());
        await _blocks.DidNotReceive().EnsureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task An_admin_who_views_as_a_school_sees_the_default_when_there_is_no_block()
    {
        _viewContext.IsImpersonating.Returns(true);
        _blocks.GetByKeyAsync(Key).Returns((ContentBlockDto?)null);

        Assert.Equal(Default, await RenderAsync());
    }
}
