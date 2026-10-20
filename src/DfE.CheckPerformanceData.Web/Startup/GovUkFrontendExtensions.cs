using DfE.CheckPerformanceData.Web.Middleware;
using GovUk.Frontend.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class GovUkFrontendExtensions
{
    /// <summary>
    /// Registers GOV.UK Frontend and hands the GDS "Error: " page-title prefix to
    /// <see cref="Common.PageTitle.WithErrorPrefix"/> alone.
    /// </summary>
    /// <remarks>
    /// The library ships a <c>TitleTagHelper</c> targeting <c>&lt;title&gt;</c> inside
    /// <c>&lt;head&gt;</c>. With <c>PrependErrorToTitle</c> (its default is <c>true</c>) it
    /// appends "Error: " to the title whenever a <c>&lt;govuk-error-summary&gt;</c> has been
    /// rendered on the page. Our layouts already apply the prefix centrally, so every error
    /// page that also rendered a govuk-error-summary came back as "Error: Error: &lt;title&gt;" —
    /// a screen reader announced the prefix twice.
    ///
    /// The layouts keep ownership rather than the tag helper because they cover more ground:
    /// the tag helper only fires for the library's own error summary component, whereas
    /// <c>PageTitle.WithErrorPrefix</c> keys off <c>ModelState</c> plus an explicit
    /// <c>ViewData["HasError"]</c> for pages whose errors are shown some other way (the
    /// journey Summary duplicate-request banner). See docs/accessibility.md.
    ///
    /// <c>GetCspNonceForRequest</c> closes the other gap the page template opened (#611): the
    /// layout's own <c>&lt;script&gt;</c> tags (the <c>js-enabled</c> class, the module import and
    /// <c>initAll()</c>) are emitted by the compiled <c>_GovUkPageTemplate</c> view, so they never
    /// pass through our <see cref="TagHelpers.CspNonceTagHelper"/>. The library stamps them for us
    /// when this delegate returns the request's nonce.
    /// </remarks>
    public static IServiceCollection AddCpdGovUkFrontend(this IServiceCollection services) =>
        services.AddGovUkFrontend(options =>
        {
            options.PrependErrorToTitle = false;
            options.GetCspNonceForRequest = CspNonce.Get;
        });
}
