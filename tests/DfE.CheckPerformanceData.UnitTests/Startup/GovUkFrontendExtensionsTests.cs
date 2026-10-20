using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.Startup;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// Guards the single owner of the GDS "Error: " page-title prefix.
//
// GovUk.Frontend.AspNetCore ships its own TitleTagHelper ([HtmlTargetElement("title",
// ParentTag = "head")]) which appends "Error: " to the <title> whenever
// GovUkFrontendOptions.PrependErrorToTitle is true (its default) and a
// <govuk-error-summary> has been rendered on the page. We already apply the prefix
// centrally in _Layout.cshtml / _AdminLayout.cshtml via PageTitle.WithErrorPrefix,
// because that also covers pages whose errors never reach a govuk-error-summary (the
// Summary conflict banner sets ViewData["HasError"]). With both switched on, every
// error page that does render a govuk-error-summary came back titled
// "Error: Error: <title>".
public class GovUkFrontendExtensionsTests
{
    [Fact]
    public void AddCpdGovUkFrontend_DisablesTheLibrarysOwnErrorTitlePrefix()
    {
        var services = new ServiceCollection();

        services.AddCpdGovUkFrontend();

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<GovUkFrontendOptions>>().Value;

        Assert.False(options.PrependErrorToTitle);
    }

    // The page template's own script tags (js-enabled, module import, initAll) come from the
    // library's compiled layout, so they never pass through CspNonceTagHelper. The library only
    // stamps them when GetCspNonceForRequest is wired to the request's nonce; left null they were
    // the four scripts the browser blocked once 'unsafe-inline' was removed (#611).
    [Fact]
    public void AddCpdGovUkFrontend_PublishesTheRequestNonceToThePageTemplate()
    {
        var services = new ServiceCollection();

        services.AddCpdGovUkFrontend();

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<GovUkFrontendOptions>>().Value;
        var context = new DefaultHttpContext();
        context.Items[CspNonce.ItemKey] = "the-request-nonce";

        Assert.Equal("the-request-nonce", options.GetCspNonceForRequest?.Invoke(context));
    }

    [Fact]
    public void AddCpdGovUkFrontend_PageTemplateNonceIsNullWhenNoMiddlewareRan()
    {
        var services = new ServiceCollection();

        services.AddCpdGovUkFrontend();

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<GovUkFrontendOptions>>().Value;

        Assert.Null(options.GetCspNonceForRequest?.Invoke(new DefaultHttpContext()));
    }
}
