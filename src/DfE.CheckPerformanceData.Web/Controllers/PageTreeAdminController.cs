using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class PageTreeAdminController(
    IPageNodeService pageNodeService,
    PageNodePathValidator pathValidator,
    IHtmlRenderingService htmlRenderingService) : Controller
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

    // ── Versions ─────────────────────────────────────────────────────────────

    [HttpGet("/admin/pages/{id:guid}/versions")]
    public async Task<IActionResult> Versions(Guid id)
    {
        var node = await pageNodeService.GetNodeByIdAsync(id);
        if (node is null) return NotFound();

        var versions = await pageNodeService.GetVersionsAsync(id);
        var vm = new PageTreeAdminVersionsViewModel
        {
            NodeId = id,
            NodeTitle = node.Title,
            Versions = versions
        };

        return View("Versions", vm);
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    [HttpPost("/admin/pages/{id:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id, int versionId, DateTime? from, DateTime? to)
    {
        await pageNodeService.PublishAsync(id, versionId, from, to, User?.Identity?.Name);
        return Redirect($"/admin/pages/{id}/versions");
    }

    // ── Edit / Save ───────────────────────────────────────────────────────────

    [HttpGet("/admin/pages/{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var node = await pageNodeService.GetNodeByIdAsync(id);
        if (node is null) return NotFound();

        return node.PageType switch
        {
            "wiki"    => await WikiEditViewAsync(id, node),
            "content" => NotFound(), // TODO: content widget editor — next task
            _         => Redirect("/admin/pages")
        };
    }

    [HttpPost("/admin/pages/{id:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid id, string content)
    {
        var rendered = htmlRenderingService.RenderHtml(content) ?? string.Empty;
        var plain = htmlRenderingService.StripTagsToPlainText(rendered);
        await pageNodeService.SaveWorkingContentAsync(id, content, plain, User?.Identity?.Name);
        return Redirect($"/admin/pages/{id}/edit");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<IActionResult> WikiEditViewAsync(Guid id, PageNodeDto node)
    {
        var versions = await pageNodeService.GetVersionsAsync(id);
        var working = versions.FirstOrDefault(v => v.PublishFrom is null);
        var vm = new PageTreeAdminWikiEditViewModel
        {
            NodeId    = id,
            NodeTitle = node.Title,
            Content   = working?.Content ?? string.Empty
        };
        return View("WikiEdit", vm);
    }

    // ── form helpers ─────────────────────────────────────────────────────────

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
