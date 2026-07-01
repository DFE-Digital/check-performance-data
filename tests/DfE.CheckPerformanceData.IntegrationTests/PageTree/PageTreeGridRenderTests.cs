using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
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

// Renders Views/PageTreeAdmin/Index.cshtml through the real Razor view engine to prove:
//   - each child row's page-type icon partial is invoked (data-icon attribute present)
//   - the title link drills into the child's own grid at /admin/pages/{childId}
//   - action links (Edit, Versions, Delete, New child page) carry correct hrefs
//   - move-up / move-down forms post to /admin/pages/{childId}/move
//   - View link is present for a child with HasLiveVersion=true and ABSENT for false
//   - the search input renders with the correct name attribute
public sealed class PageTreeGridRenderTests
{
    [Fact]
    public async Task RendersChildRows_WithIconDrillInLinksActionsAndViewVisibility()
    {
        var selectedId = Guid.NewGuid();
        var liveId     = Guid.NewGuid();
        var draftId    = Guid.NewGuid();

        var vm = new PageTreeGridViewModel
        {
            SelectedId    = selectedId,
            SelectedTitle = "My Section",
            SelectedPath  = "my-section",
            SelectedPageType  = "folder",
            SelectedParentId  = null,
            SelectedHasLiveVersion = false,
            SearchQuery   = null,
            CurrentPage   = 1,
            TotalPages    = 1,
            TotalCount    = 2,
            PageSize      = 20,
            Children      = new List<PageTreeGridRowViewModel>
            {
                new()
                {
                    Id             = liveId,
                    Title          = "Live child",
                    Segment        = "live-child",
                    Path           = "my-section/live-child",
                    PageType       = "content",
                    HasLiveVersion = true
                },
                new()
                {
                    Id             = draftId,
                    Title          = "Draft child",
                    Segment        = "draft-child",
                    Path           = "my-section/draft-child",
                    PageType       = "wiki",
                    HasLiveVersion = false
                }
            }
        };

        var html = await RenderIndexAsync(vm);

        // ── Icons ──────────────────────────────────────────────────────────────
        // content → data-icon="widgets"
        Assert.Contains("data-icon=\"widgets\"", html);
        // wiki → data-icon="menu_book"
        Assert.Contains("data-icon=\"menu_book\"", html);

        // ── Drill-in title links ───────────────────────────────────────────────
        Assert.Contains($"/admin/pages/{liveId}\"", html);
        Assert.Contains($"/admin/pages/{draftId}\"", html);

        // ── Action links ───────────────────────────────────────────────────────
        Assert.Contains($"/admin/pages/{liveId}/edit",     html);
        Assert.Contains($"/admin/pages/{liveId}/versions", html);
        Assert.Contains($"/admin/pages/{liveId}/delete",   html);
        Assert.Contains($"/admin/pages/new?parentId={liveId}", html);

        // ── Move forms ────────────────────────────────────────────────────────
        Assert.Contains($"action=\"/admin/pages/{liveId}/move\"", html);
        Assert.Contains("name=\"direction\" value=\"up\"",   html);
        Assert.Contains("name=\"direction\" value=\"down\"", html);

        // ── View link: present for live, absent for draft ─────────────────────
        // href="/{path}" only renders when HasLiveVersion=true
        Assert.Contains($"href=\"/my-section/live-child\"",  html);
        Assert.DoesNotContain($"href=\"/my-section/draft-child\"", html);

        // ── Search input ──────────────────────────────────────────────────────
        Assert.Contains("name=\"q\"", html);
    }

    [Fact]
    public async Task RendersPager_WhenTotalPagesGreaterThanOne()
    {
        var nodeId  = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var vm = new PageTreeGridViewModel
        {
            SelectedId    = nodeId,
            SelectedTitle = "Section",
            SelectedPath  = "section",
            SelectedPageType  = "folder",
            SelectedParentId  = null,
            SelectedHasLiveVersion = false,
            SearchQuery   = null,
            CurrentPage   = 1,
            TotalPages    = 3,
            TotalCount    = 6,
            PageSize      = 2,
            Children      = new List<PageTreeGridRowViewModel>
            {
                new() { Id = childId, Title = "Item 1", Segment = "item-1", Path = "section/item-1", PageType = "content", HasLiveVersion = false },
                new() { Id = Guid.NewGuid(), Title = "Item 2", Segment = "item-2", Path = "section/item-2", PageType = "content", HasLiveVersion = false }
            }
        };

        var html = await RenderIndexAsync(vm);

        // govuk-pagination should appear when TotalPages > 1
        Assert.Contains("govuk-pagination", html);
        // Page 2 link must carry page=2 query param
        Assert.Contains("page=2", html);
        // Previous should NOT appear on page 1
        Assert.DoesNotContain("rel=\"prev\"", html);
        // Next should appear
        Assert.Contains("rel=\"next\"", html);
    }

    [Fact]
    public async Task RendersEmptyState_WhenNoChildren()
    {
        var vm = new PageTreeGridViewModel
        {
            SelectedId    = null,
            SelectedTitle = "All pages",
            SelectedPath  = null,
            SelectedPageType  = null,
            SelectedParentId  = null,
            SelectedHasLiveVersion = false,
            SearchQuery   = null,
            CurrentPage   = 1,
            TotalPages    = 1,
            TotalCount    = 0,
            PageSize      = 20,
            Children      = Array.Empty<PageTreeGridRowViewModel>()
        };

        var html = await RenderIndexAsync(vm);

        Assert.Contains("No pages in this section yet.", html);
        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public async Task RendersSearchEmptyState_WhenQueryYieldsNoResults()
    {
        var vm = new PageTreeGridViewModel
        {
            SelectedId    = null,
            SelectedTitle = "All pages",
            SelectedPath  = null,
            SelectedPageType  = null,
            SelectedParentId  = null,
            SelectedHasLiveVersion = false,
            SearchQuery   = "ghost",
            CurrentPage   = 1,
            TotalPages    = 1,
            TotalCount    = 0,
            PageSize      = 20,
            Children      = Array.Empty<PageTreeGridRowViewModel>()
        };

        var html = await RenderIndexAsync(vm);

        Assert.Contains("No pages match", html);
        Assert.Contains("ghost", html);
        Assert.DoesNotContain("No pages in this section yet.", html);
    }

    // Spins up a minimal MVC host with the real Razor view engine, then renders
    // Views/PageTreeAdmin/Index.cshtml with isMainPage:false so the layout is skipped.
    private static async Task<string> RenderIndexAsync(PageTreeGridViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(GuidanceController).Assembly);
                    // Index.cshtml renders move-up/down forms which need GovUk tag helpers.
                    services.AddGovUkFrontend();
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
        routeData.Values["controller"] = "PageTreeAdmin";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // isMainPage:false → skips _ViewStart / layout; renders the Index body only.
        var view = viewEngine.FindView(actionContext, "Index", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate PageTreeAdmin/Index view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<PageTreeGridViewModel>(
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
