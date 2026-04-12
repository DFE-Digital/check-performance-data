using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ContentBlockController(IContentBlockService contentBlockService) : Controller
{
    [HttpPost("content-block/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveContentBlockFormModel model)
    {
        if (!ModelState.IsValid)
            return Redirect(model.ReturnUrl ?? "/");

        await contentBlockService.SaveAsync(new SaveContentBlockDto
        {
            Key = model.Key,
            BlockType = model.BlockType,
            Value = model.Value
        });

        var returnUrl = RemoveEditParam(model.ReturnUrl ?? "/");
        return Redirect(returnUrl);
    }

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
        return Redirect(returnUrl ?? "/");
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
