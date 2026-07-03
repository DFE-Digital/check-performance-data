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
            return await NotFoundPageAsync();

        if (node.PageType == "folder")
            return await BuildFolderViewAsync(node);

        // content / wiki: try to get the live (published) version first.
        var result = await pageNodes.GetLivePageAsync(path, DateTime.UtcNow);
        if (result is not null)
        {
            return result.Node.PageType switch
            {
                "content" => await BuildContentViewAsync(result),
                "wiki"    => await BuildWikiViewAsync(result),
                _         => await NotFoundPageAsync()
            };
        }

        // No live version: editors see a draft preview; everyone else gets the 404 page.
        if (!User.IsInRole(WikiConstants.EditorRole))
            return await NotFoundPageAsync();

        return node.PageType switch
        {
            "content" => await BuildPreviewContentViewAsync(node),
            "wiki"    => await BuildPreviewWikiViewAsync(node),
            _         => await NotFoundPageAsync()
        };
    }

    // Renders /help/not-found (a CMS-authored content page) with HTTP 404 status. Falls back to
    // a bare NotFound() when the page has been deleted or has no live version, so the resolver
    // never fails to return SOMETHING.
    private async Task<IActionResult> NotFoundPageAsync()
    {
        var live = await pageNodes.GetLivePageAsync("help/not-found", DateTime.UtcNow);
        if (live is null || live.Node.PageType != "content")
        {
            Response.StatusCode = 404;
            return NotFound();
        }
        var view = await BuildContentViewAsync(live);
        Response.StatusCode = 404;
        return view;
    }

    // Ancestor chain from root down to the immediate parent, as breadcrumb items. Leaves the
    // current page off so the view can render it as the trailing plain-text crumb.
    private async Task<IReadOnlyList<BreadcrumbItem>> BuildBreadcrumbAsync(PageNodeDto node)
    {
        if (node.ParentId is null) return [];
        var tree = await pageNodes.GetTreeAsync() ?? [];
        var byId = tree.ToDictionary(n => n.Id);

        var chain = new List<BreadcrumbItem>();
        var cursor = node.ParentId;
        var depth = 0;
        while (cursor is Guid id && byId.TryGetValue(id, out var ancestor) && depth < 10)
        {
            chain.Insert(0, new BreadcrumbItem(ancestor.Title, "/" + ancestor.Path));
            cursor = ancestor.ParentId;
            depth++;
        }
        return chain;
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
    private async Task<IActionResult> BuildContentViewAsync(LivePageResult result)
    {
        var tree = ContentPageJson.Deserialize(result.Version.Content) ?? [];
        return View("Content", new RenderedPageViewModel
        {
            Title       = result.Node.Title,
            Subtitle    = result.Node.Subtitle,
            PageType    = "content",
            Content     = tree,
            Nav         = ContentNavBuilder.Build(tree),
            ChildrenNav = await BuildChildrenNavAsync(result.Node.Id),
            Breadcrumb  = await BuildBreadcrumbAsync(result.Node)
        });
    }

    // Direct-children nav for the current node, ordered by SortOrder. Empty when the node
    // has no children. Fed to the pagenav widget through ViewData.
    private async Task<IReadOnlyList<ContentNavItem>> BuildChildrenNavAsync(Guid nodeId)
    {
        var tree = await pageNodes.GetTreeAsync() ?? [];
        return tree
            .Where(n => n.ParentId == nodeId)
            .OrderBy(n => n.SortOrder)
            .Select(n => new ContentNavItem(n.Title, "/" + n.Path, []))
            .ToList();
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
            Title       = node.Title,
            Subtitle    = node.Subtitle,
            PageType    = "content",
            Content     = tree,
            Nav         = ContentNavBuilder.Build(tree),
            ChildrenNav = await BuildChildrenNavAsync(node.Id),
            Breadcrumb  = await BuildBreadcrumbAsync(node),
            IsPreview   = true
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
