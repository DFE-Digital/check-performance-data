using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Controllers;
using GovUk.Frontend.AspNetCore;
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

// Renders the Search widget through the real Razor view engine. The instant-search script binds to
// every form that carries the marker attribute, so a widget with instant search switched off must
// not carry it at all, not even with an empty value.
public sealed class SearchWidgetViewRenderTests
{
    [Fact]
    public async Task AnInstantWidgetCarriesTheInstantSearchMarker()
    {
        var html = await RenderAsync(instant: true);

        Assert.Contains("data-cypmd-instant-search=\"true\"", html);
    }

    [Fact]
    public async Task ANonInstantWidgetCarriesNoInstantSearchMarker()
    {
        var html = await RenderAsync(instant: false);

        Assert.DoesNotContain("data-cypmd-instant-search", html);
    }

    [Fact]
    public async Task AWidgetSavedBeforeTheOptionExistedCarriesNoInstantSearchMarker()
    {
        var html = await RenderAsync(instant: null);

        Assert.DoesNotContain("data-cypmd-instant-search", html);
    }

    private static async Task<string> RenderAsync(bool? instant)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    services.AddGovUkFrontend();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var view = viewEngine.GetView(
            executingFilePath: null,
            viewPath: "~/Views/Shared/ContentPages/Widgets/_Search.cshtml",
            isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _Search view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var props = new JsonObject();
        if (instant is { } on) props["instant"] = on;
        var model = new WidgetNode { Type = "search", Props = props };

        var dictionary = new ViewDataDictionary<WidgetNode>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, dictionary, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
