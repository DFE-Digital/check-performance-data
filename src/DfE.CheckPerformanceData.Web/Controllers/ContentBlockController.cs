using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The content-blocks management surface — inline editor, save, versions and revert. Gated by
// the content-blocks section grant.
[RequireAdminSection(AdminNavKeys.ContentBlocks)]
public sealed partial class ContentBlockController(
    IContentBlockService contentBlockService,
    IPageNodeService pageNodeService) : Controller
{
    // The content-blocks management page: every block in one place, edited inline one after
    // another. ?edit=<key> opens that block's editor (reusing the same save endpoint).
    // ?page=<request-path> filters the list to just the blocks last seen on that page —
    // used by the content-blocks tree in the left admin nav.
    [HttpGet("admin/content-blocks")]
    public async Task<IActionResult> Index(string? edit = null, string? page = null)
    {
        var allBlocks = await contentBlockService.GetAllAsync();

        var filterPath = NormaliseFilterPath(page);
        IReadOnlyList<ContentBlockDto> blocks = allBlocks;
        string? filterPageTitle = null;
        if (filterPath is not null)
        {
            var requestPath = "/" + filterPath;
            blocks = allBlocks
                .Where(b => string.Equals(b.LastSeenPath, requestPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            filterPageTitle = (await pageNodeService.GetNodeByPathAsync(filterPath))?.Title;
        }

        return View(new ContentBlocksAdminViewModel
        {
            Blocks = blocks,
            EditKey = edit,
            FilterPagePath = filterPath,
            FilterPageTitle = filterPageTitle,
            TotalBlockCount = allBlocks.Count,
        });
    }

    private static string? NormaliseFilterPath(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return null;
        var trimmed = page.Trim().TrimStart('/');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [HttpPost("content-block/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveContentBlockFormModel model)
    {
        if (!ModelState.IsValid)
            return Redirect(LocalUrl.SafeOrNull(model.ReturnUrl) ?? "/");

        await contentBlockService.SaveAsync(new SaveContentBlockDto
        {
            Key = model.Key,
            BlockType = model.BlockType,
            Value = model.Value,
            OriginalValue = model.OriginalValue
        });

        var returnUrl = RemoveEditParam(LocalUrl.SafeOrNull(model.ReturnUrl) ?? "/");
        return Redirect(AppendAnchor(returnUrl, model.Anchor));
    }

    private static string AppendAnchor(string url, string? anchor)
    {
        // Only a simple, self-referential fragment id — never user-controlled markup or a
        // second URL. Keeps the editor's scroll position without opening a redirect vector.
        if (!string.IsNullOrEmpty(anchor) && SafeAnchor().IsMatch(anchor))
        {
            return $"{url}#{anchor}";
        }
        return url;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeAnchor();

    [HttpGet("content-block/versions/{key}")]
    public async Task<IActionResult> Versions(string key)
    {
        var block = await contentBlockService.GetByKeyAsync(key);
        if (block == null) return NotFound();

        var versions = await contentBlockService.GetVersionsAsync(key);
        var vm = new ContentBlockVersionsViewModel
        {
            Block = block,
            Versions = versions
        };

        return View(vm);
    }

    [HttpPost("content-block/revert/{key}/{versionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(string key, int versionId, string? returnUrl)
    {
        await contentBlockService.RevertToVersionAsync(key, versionId);
        return Redirect(LocalUrl.SafeOrNull(returnUrl) ?? "/");
    }

    private static string RemoveEditParam(string url)
    {
        var uriIndex = url.IndexOf('?');
        if (uriIndex < 0) return url;

        var path = url[..uriIndex];
        var query = url[(uriIndex + 1)..];
        var parameters = query.Split('&')
            .Where(p => !p.StartsWith("edit=", StringComparison.OrdinalIgnoreCase) && p != "edit")
            .ToList();

        return parameters.Count > 0
            ? $"{path}?{string.Join('&', parameters)}"
            : path;
    }
}
