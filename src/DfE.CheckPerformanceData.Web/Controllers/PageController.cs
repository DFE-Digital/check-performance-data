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

        // content / wiki: try to get the live (published) version first.
        var result = await pageNodes.GetLivePageAsync(path, DateTime.UtcNow);
        if (result is not null)
        {
            return result.Node.PageType switch
            {
                "content" => BuildContentView(result),
                "wiki"    => await BuildWikiViewAsync(result),
                _         => NotFound()
            };
        }

        // No live version: editors see a draft preview; everyone else gets 404.
        if (!User.IsInRole(WikiConstants.EditorRole))
            return NotFound();

        return node.PageType switch
        {
            "content" => await BuildPreviewContentViewAsync(node),
            "wiki"    => await BuildPreviewWikiViewAsync(node),
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

    // Deserialises the content tree and builds the in-page heading nav.
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

    // Preview: editor views a content page with no live version — renders the working/latest draft.
    private async Task<IActionResult> BuildPreviewContentViewAsync(PageNodeDto node)
    {
        var content = await pageNodes.GetWorkingOrLatestContentAsync(node.Id) ?? "[]";
        var tree = ContentPageJson.Deserialize(content) ?? [];
        return View("Content", new RenderedPageViewModel
        {
            Title     = node.Title,
            PageType  = "content",
            Content   = tree,
            Nav       = ContentNavBuilder.Build(tree),
            IsPreview = true
        });
    }

    // Preview: editor views a wiki page with no live version — renders the working/latest draft.
    // WikiHtml is stored raw; sanitisation happens in Wiki.cshtml via IHtmlRenderingService.
    private async Task<IActionResult> BuildPreviewWikiViewAsync(PageNodeDto node)
    {
        var content = await pageNodes.GetWorkingOrLatestContentAsync(node.Id);
        var tree = await pageNodes.GetTreeAsync() ?? [];
        var siblings = tree
            .Where(n => n.ParentId == node.ParentId)
            .OrderBy(n => n.SortOrder)
            .Select(n => new ContentNavItem(n.Title, "/" + n.Path, []))
            .ToList();

        return View("Wiki", new RenderedPageViewModel
        {
            Title     = node.Title,
            PageType  = "wiki",
            WikiHtml  = content,
            Nav       = siblings,
            IsPreview = true
        });
    }
}
