using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Catch-all resolver: any URL not claimed by a real app route falls through here.
// Order = int.MaxValue on the HttpGet attribute guarantees this endpoint is always
// evaluated last, so it can never shadow a real route.
[AllowAnonymous]
public sealed class PageController(IPageNodeService pageNodes) : Controller
{
    [HttpGet("/{*path}", Order = int.MaxValue)]
    public async Task<IActionResult> Show(string path)
    {
        var node = await pageNodes.GetNodeByPathAsync(path);
        if (node is null)
            return NotFound();

        if (node.PageType == "folder")
            return await BuildFolderViewAsync(node);

        // content / wiki: require a live version
        var result = await pageNodes.GetLivePageAsync(path, DateTime.UtcNow);
        if (result is null)
            return NotFound();

        return result.Node.PageType switch
        {
            "content" => BuildContentView(result),
            "wiki"    => await BuildWikiViewAsync(result),
            _         => NotFound()
        };
    }

    // Builds a child index for a folder node: direct children ordered by SortOrder,
    // rendered as a GDS list of links. Nav carries the child items (same member as
    // used for heading nav in content pages and sibling nav in wiki pages).
    private async Task<IActionResult> BuildFolderViewAsync(PageNodeDto folder)
    {
        var tree     = await pageNodes.GetTreeAsync() ?? [];
        var children = tree
            .Where(n => n.ParentId == folder.Id)
            .OrderBy(n => n.SortOrder)
            .Select(n => new ContentNavItem(n.Title, "/" + n.Path, []))
            .ToList();

        return View("Folder", new RenderedPageViewModel
        {
            Title    = folder.Title,
            PageType = "folder",
            Nav      = children
        });
    }

    // Deserialises the content tree and builds the in-page heading nav, mirroring
    // GuidanceController.Show which does the same from a ContentPage payload.
    private IActionResult BuildContentView(LivePageResult result)
    {
        var tree = ContentPageJson.Deserialize(result.Version.Content) ?? [];
        return View("Content", new RenderedPageViewModel
        {
            Title    = result.Node.Title,
            PageType = "content",
            Content  = tree,
            Nav      = ContentNavBuilder.Build(tree)
        });
    }

    // Builds the section side-nav from the tree: siblings of the current node
    // (same ParentId, including the current page itself), ordered by SortOrder.
    private async Task<IActionResult> BuildWikiViewAsync(LivePageResult result)
    {
        var tree = await pageNodes.GetTreeAsync() ?? [];
        var siblings = tree
            .Where(n => n.ParentId == result.Node.ParentId)
            .OrderBy(n => n.SortOrder)
            .Select(n => new ContentNavItem(n.Title, "/" + n.Path, []))
            .ToList();

        return View("Wiki", new RenderedPageViewModel
        {
            Title    = result.Node.Title,
            PageType = "wiki",
            WikiHtml = result.Version.Content,
            Nav      = siblings
        });
    }
}
