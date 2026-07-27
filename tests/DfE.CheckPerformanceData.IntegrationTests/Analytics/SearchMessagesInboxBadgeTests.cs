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

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Renders the SearchMessagesInboxBadge component view directly through the composite view
// engine. Asserts the badge markup mirrors the DlqBadge shape (govuk-service-navigation__item
// list item wrapping a link + govuk-tag), links to /admin/Messages/Inbox, and renders the
// count from the model. Also proves the zero-count branch renders the grey tag (matches
// DlqBadge's zero styling — never break the chrome when there is nothing unread).
public sealed class SearchMessagesInboxBadgeTests
{
    [Fact]
    public async Task Badge_WithUnreadCountThree_RendersBlueTagAndLinkToInbox()
    {
        var html = await RenderBadgeAsync(unreadCount: 3);

        Assert.Contains("govuk-service-navigation__item", html);
        Assert.Contains("href=\"/admin/Messages/Inbox\"", html);
        Assert.Contains("govuk-tag", html);
        Assert.Contains(">3<", html);
        Assert.Contains("govuk-tag--blue", html);
    }

    [Fact]
    public async Task Badge_WithZeroUnread_RendersGreyTagStillLinkedToInbox()
    {
        var html = await RenderBadgeAsync(unreadCount: 0);

        Assert.Contains("govuk-service-navigation__item", html);
        Assert.Contains("href=\"/admin/Messages/Inbox\"", html);
        Assert.Contains("govuk-tag--grey", html);
        Assert.Contains(">0<", html);
    }

    // Ask the composite engine for the component view directly. The badge is a bare int
    // model view that carries no layout dependency, so isMainPage:false renders it without
    // pulling _ViewStart / _AdminLayout / the content-block dep graph.
    private static async Task<string> RenderBadgeAsync(int unreadCount)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchAnalyticsController).Assembly);
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
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(
            executingFilePath: null,
            viewPath: "/Views/Shared/Components/SearchMessagesInboxBadge/Default.cshtml",
            isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate badge view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<int>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = unreadCount };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
