using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
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

// Renders Views/Page/Wiki.cshtml through the real Razor view engine to prove:
//   - safe body HTML survives intact
//   - <script> tags are stripped by IHtmlRenderingService.RenderHtml
//   - the sibling section nav (from RenderedPageViewModel.Nav) renders via _SideNav
public sealed class WikiTemplateRenderTests
{
    [Fact]
    public async Task RendersSanitisedBodyAndSiblingNav()
    {
        IReadOnlyList<ContentNavItem> nav =
        [
            new ContentNavItem("Sibling Page", "/section/sibling", [])
        ];

        var model = new RenderedPageViewModel
        {
            Title    = "Test Wiki Page",
            PageType = "wiki",
            WikiHtml = "<p>Hi<script>alert(1)</script></p>",
            Nav      = nav
        };

        var html = await RenderWikiViewAsync(model);

        // H1 title present in GDS xl heading style.
        Assert.Contains("govuk-heading-xl", html);
        Assert.Contains("Test Wiki Page", html);

        // Safe body text survives sanitisation.
        Assert.Contains("Hi", html);

        // <script> is stripped by IHtmlRenderingService.RenderHtml.
        Assert.DoesNotContain("<script", html);

        // _SideNav partial renders the sibling nav link.
        Assert.Contains("moj-side-navigation", html);
        Assert.Contains("/section/sibling", html);
        Assert.Contains("Sibling Page", html);
    }

    [Fact]
    public async Task RendersPreviewBanner_WhenIsPreviewTrue()
    {
        var model = new RenderedPageViewModel
        {
            Title    = "Draft Wiki",
            PageType = "wiki",
            WikiHtml = "<p>Body</p>",
            Nav      = [],
            IsPreview = true
        };

        var html = await RenderWikiViewAsync(model);

        Assert.Contains("govuk-notification-banner", html);
        Assert.Contains("not published", html);
    }

    [Fact]
    public async Task DoesNotRenderPreviewBanner_WhenIsPreviewFalse()
    {
        var model = new RenderedPageViewModel
        {
            Title    = "Published Wiki",
            PageType = "wiki",
            WikiHtml = "<p>Body</p>",
            Nav      = [],
            IsPreview = false
        };

        var html = await RenderWikiViewAsync(model);

        Assert.DoesNotContain("govuk-notification-banner", html);
        Assert.DoesNotContain("not published", html);
    }

    // Spins up a minimal MVC host to obtain a wired view engine, then renders
    // Views/Page/Wiki.cshtml with isMainPage:false (bypasses _ViewStart / layout lookup).
    private static async Task<string> RenderWikiViewAsync(RenderedPageViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    // Wiki.cshtml injects the sanitiser via @inject IHtmlRenderingService.
                    services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
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

        // isMainPage:false → no _ViewStart / layout processing; just the view body.
        var view = viewEngine.FindView(actionContext, "Wiki", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Wiki view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<RenderedPageViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
