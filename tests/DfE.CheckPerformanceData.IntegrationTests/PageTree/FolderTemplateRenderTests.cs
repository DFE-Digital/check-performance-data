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

// Renders Views/Page/Folder.cshtml through the real Razor view engine to prove:
//   - the H1 title renders with the GDS xl heading class
//   - each child link (text + href) appears inside a govuk-list item
//   - when Nav is empty the "no pages yet" paragraph renders instead
public sealed class FolderTemplateRenderTests
{
    [Fact]
    public async Task RendersH1AndChildLinks()
    {
        IReadOnlyList<ContentNavItem> nav =
        [
            new ContentNavItem("FAQ",     "/support/faq",     []),
            new ContentNavItem("Contact", "/support/contact", [])
        ];

        var model = new RenderedPageViewModel
        {
            Title    = "Support",
            PageType = "folder",
            Nav      = nav
        };

        var html = await RenderFolderViewAsync(model);

        // H1 carries the folder title with GDS xl heading class.
        Assert.Contains("govuk-heading-xl", html);
        Assert.Contains("Support", html);

        // Child list is rendered as a govuk-list.
        Assert.Contains("govuk-list", html);

        // First child link.
        Assert.Contains("/support/faq", html);
        Assert.Contains("FAQ", html);

        // Second child link.
        Assert.Contains("/support/contact", html);
        Assert.Contains("Contact", html);

        // Empty-state message must NOT appear when there are children.
        Assert.DoesNotContain("no pages yet", html);
    }

    [Fact]
    public async Task RendersEmptyStateMessage_WhenNoChildren()
    {
        var model = new RenderedPageViewModel
        {
            Title    = "Empty Section",
            PageType = "folder",
            Nav      = []
        };

        var html = await RenderFolderViewAsync(model);

        Assert.Contains("govuk-heading-xl", html);
        Assert.Contains("Empty Section", html);

        // Empty-state paragraph must appear.
        Assert.Contains("no pages yet", html);

        // No list links should be present.
        Assert.DoesNotContain("govuk-link", html);
    }

    // Spins up a minimal MVC host to obtain a wired view engine, then renders
    // Views/Page/Folder.cshtml with isMainPage:false (bypasses _ViewStart / layout lookup).
    private static async Task<string> RenderFolderViewAsync(RenderedPageViewModel model)
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

        // isMainPage:false → no _ViewStart / layout processing; just the view body.
        var view = viewEngine.FindView(actionContext, "Folder", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Folder view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

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
