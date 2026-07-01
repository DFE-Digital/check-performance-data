using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class PageTreeAdminController(
    IPageNodeService pageNodeService,
    PageNodePathValidator pathValidator) : Controller
{
    [HttpGet("/admin/pages")]
    public async Task<IActionResult> Index()
    {
        var tree = PageTreeBuilder.Build(await pageNodeService.GetTreeAsync());
        return View(tree);
    }

    [HttpGet("/admin/pages/new")]
    public async Task<IActionResult> New(Guid? parentId)
    {
        string? parentTitle = null;
        if (parentId.HasValue)
        {
            var parent = await pageNodeService.GetNodeByIdAsync(parentId.Value);
            if (parent is null) return NotFound();
            parentTitle = parent.Title;
        }

        return View(new NewPageViewModel { ParentId = parentId, ParentTitle = parentTitle });
    }

    [HttpPost("/admin/pages/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid? parentId, string? pageType, string? segment, string? title)
    {
        pageType ??= string.Empty;
        segment ??= string.Empty;
        title ??= string.Empty;

        if (pageType is not ("content" or "wiki" or "folder"))
            return await FormWithError(parentId, pageType, segment, title,
                "Page type must be content, wiki, or folder.");

        PageNodeDto? parent = null;
        if (parentId.HasValue)
        {
            parent = await pageNodeService.GetNodeByIdAsync(parentId.Value);
            if (parent is null) return NotFound();
        }

        var path = parent is null ? segment : parent.Path + "/" + segment;

        var (ok, error) = pathValidator.Validate(path);
        if (!ok)
            return View("New", BuildViewModel(parentId, parent?.Title, pageType, segment, title, error));

        if (await pageNodeService.GetNodeByPathAsync(path) is not null)
            return View("New", BuildViewModel(parentId, parent?.Title, pageType, segment, title,
                "A page already exists at that path."));

        var node = await pageNodeService.CreatePageAsync(
            parentId, segment, title, pageType, User?.Identity?.Name);

        return pageType is "content" or "wiki"
            ? Redirect($"/admin/pages/{node.Id}/edit")
            : Redirect("/admin/pages");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<ViewResult> FormWithError(
        Guid? parentId, string pageType, string segment, string title, string error)
    {
        string? parentTitle = null;
        if (parentId.HasValue)
        {
            var p = await pageNodeService.GetNodeByIdAsync(parentId.Value);
            parentTitle = p?.Title;
        }
        return View("New", BuildViewModel(parentId, parentTitle, pageType, segment, title, error));
    }

    private static NewPageViewModel BuildViewModel(
        Guid? parentId, string? parentTitle, string pageType, string segment, string title, string? error) =>
        new()
        {
            ParentId = parentId,
            ParentTitle = parentTitle,
            PageType = pageType,
            Segment = segment,
            Title = title,
            Error = error
        };
}
