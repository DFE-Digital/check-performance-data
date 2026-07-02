using DfE.CheckPerformanceData.Application.PageTree;
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

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Renders Views/Shared/PageTree/_TreeNode.cshtml through the real Razor view engine to prove:
//   - the page-type icon partial is invoked (data-icon attribute present)
//   - per-node action links carry the exact /admin/pages/{id}/… hrefs
//   - recursion works: a child node's title and path appear in the output
public sealed class TreeNodeRenderTests
{
    [Fact]
    public async Task RendersIconActionLinksAndRecursesIntoChildren()
    {
        var parentId = Guid.NewGuid();
        var childId  = Guid.NewGuid();

        var child = new PageTreeNode(
            childId,
            "Child page",
            "parent/child",
            "content",
            HasLiveVersion: true,
            Children: []);

        var parent = new PageTreeNode(
            parentId,
            "Parent folder",
            "parent",
            "folder",
            HasLiveVersion: false,
            Children: [child]);

        var html = await RenderTreeNodeAsync(parent);

        // Parent is a folder — icon should be data-icon="folder".
        Assert.Contains("data-icon=\"folder\"", html);

        // Child is a content node — icon should be data-icon="widgets".
        Assert.Contains("data-icon=\"widgets\"", html);

        // Per-node action links for the parent carry the correct hrefs.
        Assert.Contains($"/admin/pages/{parentId}/edit",          html);
        Assert.Contains($"/admin/pages/{parentId}/edit#version-history", html);
        Assert.Contains($"/admin/pages/{parentId}/delete",        html);
        Assert.Contains($"/admin/pages/new?parentId={parentId}",  html);

        // Child's title and path appear — recursion rendered the child node.
        Assert.Contains("Child page",    html);
        Assert.Contains("parent/child",  html);

        // Child's action links carry the child's own id, not the parent's.
        Assert.Contains($"/admin/pages/{childId}/edit",         html);
        Assert.Contains($"/admin/pages/{childId}/edit#version-history", html);
        Assert.Contains($"/admin/pages/{childId}/delete",       html);
        Assert.Contains($"/admin/pages/new?parentId={childId}", html);
    }

    // HasLiveVersion=true → title is a link; false → title is plain text (no href to a missing page).
    [Fact]
    public async Task LiveNode_RenderesTitleAsLink_NonLiveNode_RenderesTitleAsPlainText()
    {
        var liveId   = Guid.NewGuid();
        var draftId  = Guid.NewGuid();

        var liveChild  = new PageTreeNode(liveId,  "Live page",  "live",  "content", true,  []);
        var draftChild = new PageTreeNode(draftId, "Draft page", "draft", "content", false, []);

        var parent = new PageTreeNode(
            Guid.NewGuid(), "Root", "root", "folder", false, [liveChild, draftChild]);

        var html = await RenderTreeNodeAsync(parent);

        // Live child renders as an anchor with its path.
        Assert.Contains("href=\"/live\"", html);

        // Draft child title must appear as text but NOT as an href link.
        Assert.Contains("Draft page", html);
        Assert.DoesNotContain("href=\"/draft\"", html);
    }

    // Spins up a minimal MVC host to obtain a wired view engine, then renders
    // Views/Shared/PageTree/_TreeNode.cshtml with isMainPage:false (bypasses _ViewStart / layout).
    private static async Task<string> RenderTreeNodeAsync(PageTreeNode model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(GuidanceController).Assembly);
                    // _TreeNode.cshtml renders GovUk form tag helpers (move up/down),
                    // which resolve IComponentGenerator from the GovUk.Frontend services.
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
        // Controller name drives the Razor search path: Views/PageTreeAdmin/ then Views/Shared/.
        routeData.Values["controller"] = "PageTreeAdmin";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // isMainPage:false → no _ViewStart / layout processing; just the partial body.
        var view = viewEngine.FindView(actionContext, "PageTree/_TreeNode", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _TreeNode view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<PageTreeNode>(
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
