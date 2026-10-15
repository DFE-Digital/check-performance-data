using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
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

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Renders the version history partial and its error summary through the real Razor view engine.
// Publish windows are stored in UTC and must be shown, and pre-filled, in UK time. Fixed BST and
// GMT dates keep the assertions independent of the host's zone and of today's date.
public sealed class VersionHistoryListRenderTests
{
    private static readonly Guid NodeId = Guid.Parse("0b1d3c55-6a2e-4f0e-9a51-1f0f5c3c5310");

    private static PageTreeAdminVersionHistoryModel Model() => new()
    {
        NodeId = NodeId,
        Versions =
        [
            new PageNodeVersionDto
            {
                VersionId = 3,
                Content = "[]",
                // 08:00 UTC in July is 09:00 BST; 17:00 UTC in December is 17:00 GMT.
                PublishFrom = new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc),
                PublishTo = new DateTime(2026, 12, 1, 17, 0, 0, DateTimeKind.Utc),
                UpdatedDate = new DateTime(2026, 7, 10, 11, 5, 0, DateTimeKind.Utc),
            },
        ],
    };

    [Fact]
    public async Task PrefillsPublishWindowInUkTime_AndLabelsTheFieldsUkTime()
    {
        var html = await RenderAsync("_VersionHistoryList", Model(), error: null);

        Assert.Contains("value=\"2026-07-15T09:00\"", html);
        Assert.Contains("value=\"2026-12-01T17:00\"", html);
        Assert.DoesNotContain("value=\"2026-07-15T08:00\"", html);

        Assert.Contains("Publish from (UK time)", html);
        Assert.Contains("Publish to (UK time)", html);
        Assert.Contains("id=\"publish-window-hint\"", html);
        Assert.Contains("aria-describedby=\"publish-window-hint\"", html);
        Assert.Contains("id=\"from-3\"", html);
        Assert.Contains("id=\"to-3\"", html);

        // The Modified column sits beside the window fields, so it is in UK time as well.
        Assert.Contains("10 Jul 2026 12:05", html);
        Assert.DoesNotContain("govuk-error-message", html);
    }

    [Fact]
    public async Task RejectedWindow_ShowsTheErrorOnTheRowAndKeepsWhatWasTyped()
    {
        var error = new PublishWindowError(3, "2026-03-29T01:30", "2026-04-30T17:00",
            "Publish from must be a real UK time. On 29 March 2026 the clocks go forward from 1am to 2am, so 1:30am does not exist",
            null);

        var html = await RenderAsync("_VersionHistoryList", Model(), error);

        Assert.Contains("value=\"2026-03-29T01:30\"", html);
        Assert.Contains("value=\"2026-04-30T17:00\"", html);
        Assert.Contains("govuk-input--error", html);
        Assert.Contains("govuk-error-message", html);
        Assert.Contains("so 1:30am does not exist", html);
        Assert.Contains("aria-describedby=\"publish-window-hint from-3-error\"", html);
    }

    [Fact]
    public async Task ErrorSummary_LinksToTheRejectedField()
    {
        var error = new PublishWindowError(3, null, "2026-03-29T01:00", null,
            "Publish to must be a real UK time. On 29 March 2026 the clocks go forward from 1am to 2am, so 1:00am does not exist");

        var html = await RenderAsync("_PublishWindowErrorSummary", Model(), error);

        Assert.Contains("govuk-error-summary", html);
        Assert.Contains("There is a problem", html);
        Assert.Contains("href=\"#to-3\"", html);
        Assert.Contains("so 1:00am does not exist", html);
    }

    [Fact]
    public async Task ErrorSummary_RendersNothingWithoutAnError()
    {
        var html = await RenderAsync("_PublishWindowErrorSummary", Model(), error: null);

        Assert.DoesNotContain("govuk-error-summary", html);
    }

    private static async Task<string> RenderAsync(
        string viewName, PageTreeAdminVersionHistoryModel model, PublishWindowError? error)
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
        var routeData = new RouteData();
        routeData.Values["controller"] = "PageTreeAdmin";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, viewName, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate PageTreeAdmin/{viewName}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<PageTreeAdminVersionHistoryModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        error?.Save(tempData);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
