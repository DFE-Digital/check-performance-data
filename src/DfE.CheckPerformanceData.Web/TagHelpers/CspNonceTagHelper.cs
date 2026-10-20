using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Web.TagHelpers;

/// <summary>
/// Copies the response's Content-Security-Policy nonce onto every <c>&lt;script&gt;</c> the service
/// emits (#611).
/// </summary>
/// <remarks>
/// The nonce is generated per request by <see cref="SecurityHeadersMiddleware"/> and published in
/// <c>script-src</c>. Stamping it centrally, rather than in each view, means a new page or partial
/// cannot forget it: the only way an inline script can run is if this helper put the request's
/// nonce on it. It also covers <c>&lt;script src&gt;</c> (and <c>asp-append-version</c>, because
/// <see cref="Order"/> runs last, after the built-in <c>ScriptTagHelper</c> has rewritten the
/// element).
///
/// When no nonce is present on the request - a non-web render path, or a unit test - the element
/// is left untouched and nothing throws. That is deliberate: the page still renders and the
/// browser blocks the unauthorised script, which surfaces the misconfiguration instead of hiding
/// it.
/// </remarks>
[HtmlTargetElement("script")]
public sealed class CspNonceTagHelper : TagHelper
{
    /// <summary>Runs after the built-in script TagHelper so the emitted element is final.</summary>
    public override int Order => int.MaxValue;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext? ViewContext { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var httpContext = ViewContext?.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        if (httpContext.Items[CspNonce.ItemKey] is not string nonce || string.IsNullOrEmpty(nonce))
        {
            return;
        }

        output.Attributes.SetAttribute("nonce", nonce);
    }
}
