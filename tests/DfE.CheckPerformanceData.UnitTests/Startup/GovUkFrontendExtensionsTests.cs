using DfE.CheckPerformanceData.Web.Startup;
using GovUk.Frontend.AspNetCore;
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
}
