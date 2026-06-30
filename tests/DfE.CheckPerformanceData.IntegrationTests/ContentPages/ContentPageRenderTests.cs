using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
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

namespace DfE.CheckPerformanceData.IntegrationTests.ContentPages;

// Renders the content-tree partial through the real Razor view engine to prove the recursive render
// pipeline turns a tree into the expected GDS markup: regions become grid rows, each widget renders
// via its partial, and rich-text is sanitised (no script survives) before it reaches the page.
public sealed class ContentPageRenderTests
{
    private static WidgetNode Heading(int level, string text, string anchor) => new()
    {
        Type = "heading",
        Anchor = anchor,
        Props = new JsonObject { ["level"] = level, ["text"] = text }
    };

    [Fact]
    public async Task RendersRegionsWidgetsAndSanitisesRichText()
    {
        IReadOnlyList<ContentNode> tree =
        [
            Heading(2, "Key dates", "key-dates"),
            new RegionNode
            {
                Layout = RegionLayout.Halves,
                Columns =
                [
                    [new WidgetNode { Type = "card", Props = new JsonObject { ["title"] = "Apply now", ["body"] = "Start here", ["href"] = "/apply" } }],
                    [new WidgetNode { Type = "divider" }]
                ]
            },
            new WidgetNode { Type = "richtext", Props = new JsonObject { ["html"] = "<p>Hello<script>alert(1)</script></p>" } },
            new WidgetNode { Type = "published", Props = new JsonObject { ["text"] = "Live for 2026" } },
            new RegionNode
            {
                Layout = RegionLayout.Single,
                Columns = [[new WidgetNode { Type = "summarylist", Props = new JsonObject { ["rows"] = new JsonArray { new JsonObject { ["key"] = "Opens", ["value"] = "1 June" } } } }]]
            }
        ];

        var html = await RenderContentNodesAsync(tree);

        // Heading widget → GDS heading with its anchor id.
        Assert.Contains("id=\"key-dates\"", html);
        Assert.Contains("Key dates", html);
        Assert.Contains("govuk-heading-l", html);

        // Halves region → two one-half columns.
        Assert.Equal(2, CountOccurrences(html, "govuk-grid-column-one-half"));

        // Card, divider, published, summary-list widgets all render their GDS markup.
        Assert.Contains("dfe-card", html);
        Assert.Contains("Apply now", html);
        Assert.Contains("href=\"/apply\"", html);
        Assert.Contains("govuk-section-break", html);
        Assert.Contains("govuk-notification-banner", html);
        Assert.Contains("Live for 2026", html);
        Assert.Contains("govuk-summary-list", html);
        Assert.Contains("Opens", html);
        Assert.Contains("1 June", html);

        // Rich text is sanitised: the text survives, the script does not.
        Assert.Contains("Hello", html);
        Assert.DoesNotContain("<script", html);
    }

    private static int CountOccurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

    // Spins up a minimal MVC host to obtain a wired view engine, then renders the shared
    // ContentPages/_ContentNodes partial (which recurses into _Widget and the per-widget partials).
    private static async Task<string> RenderContentNodesAsync(IReadOnlyList<ContentNode> tree)
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
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "Guidance";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, "ContentPages/_ContentNodes", isMainPage: false);
        Assert.True(view.Success, $"Could not locate _ContentNodes. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<IReadOnlyList<ContentNode>>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = tree
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
