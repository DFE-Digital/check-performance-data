using System.Text.Json.Nodes;
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

// Renders Views/Page/Content.cshtml through the real Razor view engine to prove the template
// correctly wires up RenderedPageViewModel: H1 heading, left-hand side-nav from Nav,
// widget tree via _ContentNodes, and rich-text sanitisation (no script survives).
public sealed class ContentTemplateRenderTests
{
    [Fact]
    public async Task RendersHeadingNavWidgetsAndSanitisesRichText()
    {
        IReadOnlyList<ContentNode> content =
        [
            new WidgetNode
            {
                Type   = "heading",
                Anchor = "overview",
                Props  = new JsonObject { ["level"] = 2, ["text"] = "Overview" }
            },
            new WidgetNode
            {
                Type  = "richtext",
                Props = new JsonObject { ["html"] = "<p>Safe text<script>alert(1)</script></p>" }
            }
        ];

        IReadOnlyList<ContentNavItem> nav =
        [
            new ContentNavItem("Overview", "#overview", [])
        ];

        var model = new RenderedPageViewModel
        {
            Title    = "Test Content Page",
            PageType = "content",
            Content  = content,
            Nav      = nav
        };

        var html = await RenderContentViewAsync(model);

        // View's own H1 carries the page title in GDS xl heading style.
        Assert.Contains("govuk-heading-xl", html);
        Assert.Contains("Test Content Page", html);

        // _SideNav partial rendered from Nav.
        Assert.Contains("moj-side-navigation", html);
        Assert.Contains("#overview", html);

        // _Heading widget rendered via _ContentNodes → _Widget → _Heading.
        Assert.Contains("govuk-heading-l", html);
        Assert.Contains("id=\"overview\"", html);
        Assert.Contains("Overview", html);

        // Rich text: safe text survives; script is stripped.
        Assert.Contains("Safe text", html);
        Assert.DoesNotContain("<script", html);
    }

    // Spins up a minimal MVC host to obtain a wired view engine, then renders
    // Views/Page/Content.cshtml with isMainPage:false (bypasses _ViewStart / layout lookup).
    private static async Task<string> RenderContentViewAsync(RenderedPageViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(GuidanceController).Assembly);
                    // The rich-text partial injects the sanitiser.
                    services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine      = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData   = new RouteData();
        routeData.Values["controller"] = "Page";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // isMainPage:false → no _ViewStart / layout processing; just the view body.
        var view = viewEngine.FindView(actionContext, "Content", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Content view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<RenderedPageViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer      = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
