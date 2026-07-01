using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class PageTreeAdminController(
    IPageNodeService pageNodeService,
    PageNodePathValidator pathValidator,
    IHtmlRenderingService htmlRenderingService,
    IPageNodeContentEditor nodeContentEditor,
    ISettingService settingService) : Controller
{
    private const int DefaultPageLength = 20;

    [HttpGet("/admin/pages")]
    [HttpGet("/admin/pages/{id:guid}")]
    public async Task<IActionResult> Index(Guid? id, string? q, int page = 1)
    {
        // Resolve the selected node (null = root).
        PageNodeDto? selected = null;
        if (id.HasValue)
        {
            selected = await pageNodeService.GetNodeByIdAsync(id.Value);
            if (selected is null) return NotFound();
        }

        // Direct children of the selected node, ordered by SortOrder.
        var allItems = await pageNodeService.GetTreeAsync();
        IEnumerable<PageNodeTreeItemDto> children = allItems
            .Where(n => n.ParentId == id)
            .OrderBy(n => n.SortOrder);

        // Optional search filter on Title or Segment (case-insensitive).
        var trimmedQ = q?.Trim();
        if (!string.IsNullOrEmpty(trimmedQ))
        {
            children = children.Where(n =>
                n.Title.Contains(trimmedQ, StringComparison.OrdinalIgnoreCase) ||
                n.Segment.Contains(trimmedQ, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = children.ToList();
        var totalCount = filtered.Count;

        // Paging.
        var rawSize = await settingService.GetIntAsync(SettingKeys.WikiPageLength);
        var pageSize = rawSize > 0 ? rawSize : DefaultPageLength;
        var safePage = page < 1 ? 1 : page;
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize);

        var pageRows = filtered
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new PageTreeGridRowViewModel
            {
                Id             = n.Id,
                Title          = n.Title,
                Segment        = n.Segment,
                Path           = n.Path,
                PageType       = n.PageType,
                HasLiveVersion = n.HasLiveVersion
            })
            .ToList();

        var selectedItem = id.HasValue
            ? allItems.FirstOrDefault(n => n.Id == id.Value)
            : null;

        var vm = new PageTreeGridViewModel
        {
            SelectedId             = id,
            SelectedTitle          = selected?.Title ?? "All pages",
            SelectedPath           = selected?.Path,
            SelectedPageType       = selected?.PageType,
            SelectedParentId       = selected?.ParentId,
            SelectedHasLiveVersion = selectedItem?.HasLiveVersion ?? false,
            Children               = pageRows,
            SearchQuery            = trimmedQ,
            CurrentPage            = safePage,
            TotalPages             = totalPages,
            TotalCount             = totalCount,
            PageSize               = pageSize
        };

        return View(vm);
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
            "content" => await ContentEditViewAsync(id, node),
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

    // ── Node delete ───────────────────────────────────────────────────────────

    [HttpGet("/admin/pages/{id:guid}/delete")]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var node = await pageNodeService.GetNodeByIdAsync(id);
        if (node is null) return NotFound();

        var tree = await pageNodeService.GetTreeAsync();
        var hasChildren = tree.Any(n => n.ParentId == id);

        return View("Delete", new PageNodeDeleteViewModel
        {
            Id = id,
            Title = node.Title,
            HasChildren = hasChildren
        });
    }

    [HttpPost("/admin/pages/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePost(Guid id)
    {
        var node = await pageNodeService.GetNodeByIdAsync(id);
        if (node is null) return NotFound();

        try
        {
            await pageNodeService.DeleteAsync(id, User?.Identity?.Name);
            return Redirect("/admin/pages");
        }
        catch (InvalidOperationException)
        {
            var tree = await pageNodeService.GetTreeAsync();
            var hasChildren = tree.Any(n => n.ParentId == id);
            return View("Delete", new PageNodeDeleteViewModel
            {
                Id = id,
                Title = node.Title,
                HasChildren = hasChildren,
                Error = "Cannot delete: remove or move its child pages first."
            });
        }
    }

    // ── Node reorder ──────────────────────────────────────────────────────────

    [HttpPost("/admin/pages/{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(Guid id, string direction)
    {
        if (direction is not ("up" or "down"))
            return BadRequest();
        if (await pageNodeService.GetNodeByIdAsync(id) is null) return NotFound();
        await pageNodeService.MoveAsync(id, direction);
        return Redirect("/admin/pages");
    }

    // ── Content widget-editor mutation routes ─────────────────────────────────
    // All four routes are namespaced under /content/ so they cannot collide with
    // the node-level page-delete route /admin/pages/{id}/delete.
    // ActionBase for the node editor = /admin/pages/{id}/content.

    [HttpPost("/admin/pages/{id:guid}/content/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContentAdd(Guid id, string path, string? widgetType, string? regionLayout)
    {
        // M-01: guard against fabricated GUIDs reaching the content editor
        if (await pageNodeService.GetNodeByIdAsync(id) is null) return NotFound();

        var steps = TreePath.Parse(path);

        if (!string.IsNullOrEmpty(widgetType))
            await nodeContentEditor.AddWidgetAsync(id, steps, widgetType, User?.Identity?.Name);
        else if (Enum.TryParse<RegionLayout>(regionLayout, out var layout))
            await nodeContentEditor.AddRegionAsync(id, steps, layout, User?.Identity?.Name);

        return Redirect($"/admin/pages/{id}/edit");
    }

    [HttpPost("/admin/pages/{id:guid}/content/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContentMove(Guid id, string path, string direction)
    {
        // M-01: guard against fabricated GUIDs reaching the content editor
        if (await pageNodeService.GetNodeByIdAsync(id) is null) return NotFound();

        var steps = TreePath.Parse(path);

        if (direction == "up")
            await nodeContentEditor.MoveUpAsync(id, steps, User?.Identity?.Name);
        else
            await nodeContentEditor.MoveDownAsync(id, steps, User?.Identity?.Name);

        return Redirect($"/admin/pages/{id}/edit");
    }

    [HttpPost("/admin/pages/{id:guid}/content/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContentDelete(Guid id, string path)
    {
        // M-01: guard against fabricated GUIDs reaching the content editor
        if (await pageNodeService.GetNodeByIdAsync(id) is null) return NotFound();

        await nodeContentEditor.DeleteAsync(id, TreePath.Parse(path), User?.Identity?.Name);
        return Redirect($"/admin/pages/{id}/edit");
    }

    [HttpPost("/admin/pages/{id:guid}/content/widget")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContentWidget(Guid id, string path, string type, [FromForm] Dictionary<string, string?>? props)
    {
        // M-01: guard against fabricated GUIDs reaching the content editor
        if (await pageNodeService.GetNodeByIdAsync(id) is null) return NotFound();

        var built = WidgetPropsBuilder.Build(type, props ?? new Dictionary<string, string?>());
        await nodeContentEditor.UpdateWidgetAsync(id, TreePath.Parse(path), built, User?.Identity?.Name);
        return Redirect($"/admin/pages/{id}/edit");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<IActionResult> WikiEditViewAsync(Guid id, PageNodeDto node)
    {
        // Load draft if one exists, else the latest published version's content.
        // Avoids editing a blank document after a wiki page has been published (no draft remains).
        var content = await pageNodeService.GetWorkingOrLatestContentAsync(id);
        var vm = new PageTreeAdminWikiEditViewModel
        {
            NodeId    = id,
            NodeTitle = node.Title,
            Content   = content ?? string.Empty
        };
        return View("WikiEdit", vm);
    }

    private async Task<IActionResult> ContentEditViewAsync(Guid id, PageNodeDto node)
    {
        var json = await pageNodeService.GetWorkingOrLatestContentAsync(id) ?? "[]";
        var tree = ContentPageJson.Deserialize(json) ?? [];
        return View("~/Views/ContentPage/Edit.cshtml", new ContentPageEditViewModel
        {
            ActionBase = $"/admin/pages/{id}/content",
            Title = node.Title,
            Content = tree,
            ShowInlinePublish = false
        });
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
