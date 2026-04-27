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

    // --- StripTagsToPlainText ---

    [Fact]
    public void StripTagsToPlainText_ReturnsEmpty_WhenInputNull()
    {
        Assert.Equal(string.Empty, _sut.StripTagsToPlainText(null));
    }

    [Fact]
    public void StripTagsToPlainText_ReturnsEmpty_WhenInputEmpty()
    {
        Assert.Equal(string.Empty, _sut.StripTagsToPlainText(""));
    }

    [Fact]
    public void StripTagsToPlainText_StripsSimpleTags()
    {
        var input = "<p>Hello <strong>world</strong>.</p>";
        var result = _sut.StripTagsToPlainText(input);
        // NB expected value: "Hello world ." (space between "world" and ".").
        // Each tag substitutes to a single space, then the whitespace-collapse regex reduces
        // multiple spaces to one but does NOT join tokens across punctuation:
        //   "<p>Hello <strong>world</strong>.</p>"
        //     -> " Hello  world . "   (tag -> space)
        //     -> " Hello world . "    (whitespace collapse)
        //     -> "Hello world ."      (Trim)
        // If you see "Hello world." (no space) the regex was changed - verify
        // StripTagsToPlainText still wraps tag substitution in a space.
        Assert.Equal("Hello world .", result);
    }

    [Fact]
    public void StripTagsToPlainText_StripsSelfClosingAndAttributedTags()
    {
        var input = "<img src=\"cat.png\" alt=\"cat\"/><br/>A line.";
        var result = _sut.StripTagsToPlainText(input);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Contains("A line.", result);
    }

    [Fact]
    public void StripTagsToPlainText_CollapsesWhitespace()
    {
        var input = "<p>Line one.</p>\n\n<p>   Line   two.</p>";
        var result = _sut.StripTagsToPlainText(input);
        Assert.Equal("Line one. Line two.", result);
    }

    [Fact]
    public void StripTagsToPlainText_LeavesAlreadyPlainTextUnchanged()
    {
        var input = "No tags here.";
        var result = _sut.StripTagsToPlainText(input);
        Assert.Equal("No tags here.", result);
    }

    [Fact]
    public void StripTagsToPlainText_OnSanitisedScriptPayload_LeavesOnlyInnerText()
    {
        // The sanitiser normally removes <script> entirely - but if somehow a plain-text form
        // leaks through (e.g. in a legacy row), StripTagsToPlainText must still neutralise the tags.
        var input = "<p>before</p><script>alert(1)</script><p>after</p>";
        var result = _sut.StripTagsToPlainText(input);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Contains("alert(1)", result);
        Assert.Contains("before", result);
        Assert.Contains("after", result);
    }

    // --- GOV.UK Frontend attribute pass-through ---

    [Fact]
    public void RenderHtml_PreservesGovukWarningTextClassesAndAria()
    {
        // Contract: GOV.UK Frontend components depend on class + aria-* attributes being
        // preserved so the design-system CSS and behaviour modules apply at render time.
        var input =
            "<div class=\"govuk-warning-text\">" +
                "<span class=\"govuk-warning-text__icon\" aria-hidden=\"true\">!</span>" +
                "<strong class=\"govuk-warning-text__text\">" +
                    "<span class=\"govuk-visually-hidden\">Warning</span>" +
                    "Test message" +
                "</strong>" +
            "</div>";

        var result = _sut.RenderHtml(input);

        Assert.NotNull(result);
        Assert.Contains("class=\"govuk-warning-text\"", result);
        Assert.Contains("class=\"govuk-warning-text__icon\"", result);
        Assert.Contains("aria-hidden=\"true\"", result);
        Assert.Contains("class=\"govuk-warning-text__text\"", result);
        Assert.Contains("class=\"govuk-visually-hidden\"", result);
    }

    [Fact]
    public void RenderHtml_StillStripsScriptDespiteAttributePassthrough()
    {
        // Belt-and-suspenders: loosening attribute allow-list must not weaken XSS protection.
        var input = "<div class=\"govuk-warning-text\">ok</div><script>alert(1)</script>";
        var result = _sut.RenderHtml(input);

        Assert.NotNull(result);
        Assert.Contains("govuk-warning-text", result);
        Assert.DoesNotContain("<script", result);
        Assert.DoesNotContain("alert(1)", result);
    }
}
