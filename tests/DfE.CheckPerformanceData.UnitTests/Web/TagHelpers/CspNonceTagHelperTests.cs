using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.TagHelpers;

// One TagHelper stamps every <script> with the request's nonce, so no view can forget it and a
// new page cannot silently ship an unauthorised inline script (FR-003/FR-012).
public sealed class CspNonceTagHelperTests
{
    [Fact]
    public void Process_InlineScript_GetsTheNonce()
    {
        var output = Process(nonce: "abc123", childHtml: "window.x = 1;");

        Assert.Equal("abc123", output.Attributes["nonce"].Value);
    }

    // src-based scripts get the nonce too: it keeps the policy free of a "why does this one not
    // have a nonce" question and is the precondition for ever adding 'strict-dynamic'.
    [Fact]
    public void Process_ScriptWithSrc_GetsTheNonce()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CspNonce.ItemKey] = "abc123";
        var output = NewOutput();
        output.Attributes.SetAttribute("src", "/js/site.js");

        NewSut(httpContext).Process(NewContext(), output);

        Assert.Equal("abc123", output.Attributes["nonce"].Value);
        Assert.Equal("/js/site.js", output.Attributes["src"].Value);
    }

    // Every script in one response shares that response's nonce.
    [Fact]
    public void Process_TwoScriptsInTheSameRequest_ShareTheNonce()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CspNonce.ItemKey] = "shared-nonce";
        var sut = NewSut(httpContext);

        var first = NewOutput();
        var second = NewOutput();
        sut.Process(NewContext(), first);
        sut.Process(NewContext(), second);

        Assert.Equal("shared-nonce", first.Attributes["nonce"].Value);
        Assert.Equal(first.Attributes["nonce"].Value, second.Attributes["nonce"].Value);
    }

    // Fail-closed: with no nonce on the request (a unit test, a non-web render path) the element
    // is left untouched and nothing throws. The browser then blocks the script, which surfaces
    // the configuration error rather than hiding it.
    [Fact]
    public void Process_NoNonceOnTheRequest_LeavesTheElementUnchangedAndDoesNotThrow()
    {
        var output = NewOutput(childHtml: "window.x = 1;");

        NewSut(new DefaultHttpContext()).Process(NewContext(), output);

        Assert.False(output.Attributes.ContainsName("nonce"));
        Assert.Equal("window.x = 1;", output.Content.GetContent());
    }

    [Fact]
    public void Process_NoViewContext_LeavesTheElementUnchangedAndDoesNotThrow()
    {
        var output = NewOutput();

        new CspNonceTagHelper().Process(NewContext(), output);

        Assert.False(output.Attributes.ContainsName("nonce"));
    }

    [Fact]
    public void Process_EmptyNonce_LeavesTheElementUnchanged()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CspNonce.ItemKey] = "";

        var output = NewOutput();
        NewSut(httpContext).Process(NewContext(), output);

        Assert.False(output.Attributes.ContainsName("nonce"));
    }

    // --- helpers ---

    private static CspNonceTagHelper NewSut(HttpContext httpContext) =>
        new() { ViewContext = new ViewContext { HttpContext = httpContext } };

    private static TagHelperOutput Process(string nonce, string childHtml)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CspNonce.ItemKey] = nonce;
        var output = NewOutput(childHtml);
        NewSut(httpContext).Process(NewContext(), output);
        return output;
    }

    private static TagHelperContext NewContext() =>
        new(
            tagName: "script",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: Guid.NewGuid().ToString("N"));

    private static TagHelperOutput NewOutput(string childHtml = "")
    {
        var output = new TagHelperOutput(
            tagName: "script",
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        output.Content.SetHtmlContent(childHtml);
        return output;
    }
}
