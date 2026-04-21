using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public sealed class HtmlRenderingServiceTests
{
    private readonly HtmlRenderingService _sut = new();

    // --- Null / empty input ---

    [Fact]
    public void RenderHtml_ReturnsNull_WhenContentIsNull()
    {
        Assert.Null(_sut.RenderHtml(null));
    }

    [Fact]
    public void RenderHtml_ReturnsNull_WhenContentIsEmpty()
    {
        Assert.Null(_sut.RenderHtml(""));
    }

    // --- Markdown rendering ---

    [Fact]
    public void RenderHtml_ConvertsMarkdownToHtml()
    {
        var result = _sut.RenderHtml("**bold**");

        Assert.Contains("<strong>bold</strong>", result);
    }

    [Fact]
    public void RenderHtml_ConvertsMarkdownHeading()
    {
        var result = _sut.RenderHtml("# Heading");

        Assert.Contains("<h1>Heading</h1>", result);
    }

    [Fact]
    public void RenderHtml_ConvertsMarkdownLink()
    {
        var result = _sut.RenderHtml("[text](https://example.com)");

        Assert.Contains("<a", result);
        Assert.Contains("href=\"https://example.com\"", result);
    }

    // --- HTML passthrough ---

    [Fact]
    public void RenderHtml_PassesThroughExistingHtml()
    {
        var html = "<p>Already HTML</p>";

        var result = _sut.RenderHtml(html);

        Assert.Contains("Already HTML", result);
    }

    [Fact]
    public void RenderHtml_PassesThroughHtmlWithAttributes()
    {
        var html = "<div class=\"info\">Content</div>";

        var result = _sut.RenderHtml(html);

        Assert.Contains("Content", result);
    }

    // --- Sanitisation ---

    [Fact]
    public void RenderHtml_SanitisesScriptTags()
    {
        var result = _sut.RenderHtml("<script>alert('xss')</script>");

        Assert.DoesNotContain("<script", result);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void RenderHtml_SanitisesOnEventHandlers()
    {
        var result = _sut.RenderHtml("<img src=\"x\" onerror=\"alert(1)\">");

        Assert.DoesNotContain("onerror", result);
    }

    [Fact]
    public void RenderHtml_AllowsSafeHtmlElements()
    {
        var html = "<p><strong>Bold</strong> and <em>italic</em></p>";

        var result = _sut.RenderHtml(html);

        Assert.Contains("<strong>Bold</strong>", result);
        Assert.Contains("<em>italic</em>", result);
    }
}
