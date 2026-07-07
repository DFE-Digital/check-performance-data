using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Renders Views/Shared/PageTree/_PageTypeIcon.cshtml through the real Razor view engine
// to prove: correct data-icon attribute per page type, aria-hidden on the SVG,
// and a visually-hidden label for screen readers. Assertions use data-icon rather than
// SVG path data so the tests are decoupled from exact glyph strings.
public sealed class PageTypeIconRenderTests
{
    [Theory]
    [InlineData("content", "widgets",   "Content page")]
    [InlineData("wiki",    "menu_book", "Wiki page")]
    [InlineData("folder",  "folder",    "Folder")]
    [InlineData("unknown", "folder",    "Page")]
    public async Task RendersCorrectIconAndAccessibleLabel(
        string pageType, string expectedIcon, string expectedLabel)
    {
        var html = await RenderPartialAsync(pageType);

        // Wrapping span carries data-icon for CSS hooks and test assertions.
        Assert.Contains($"data-icon=\"{expectedIcon}\"", html);

        // SVG is decorative — must be hidden from assistive technology.
        Assert.Contains("aria-hidden=\"true\"", html);

        // Visually-hidden span provides the accessible label.
        Assert.Contains("govuk-visually-hidden", html);
        Assert.Contains(expectedLabel, html);
    }

    private static async Task<string> RenderPartialAsync(string pageType)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine       = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData   = new RouteData();
        routeData.Values["controller"] = "Page";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // isMainPage:false → no _ViewStart / layout processing; partial body only.
        // View engine searches Views/Page/PageTree/ then Views/Shared/PageTree/.
        var view = viewEngine.FindView(actionContext, "PageTree/_PageTypeIcon", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _PageTypeIcon view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<string>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = pageType
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
