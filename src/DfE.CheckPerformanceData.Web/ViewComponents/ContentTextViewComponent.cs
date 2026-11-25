using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Impersonation;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

/// <summary>
/// Shows a plain-text content block as encoded text, with no element around it and no edit link.
/// Use it where only text can go (a label, a details summary) and in a form, where the in-place
/// editor cannot go. An editor changes the block on the admin content blocks page. A block with no
/// text shows the default.
/// </summary>
public sealed class ContentTextViewComponent(IContentBlockService contentBlockService,
    IEstablishmentViewContext? viewContext = null) : ViewComponent
{
    private const string PlainTextBlockType = "Title";

    public async Task<IViewComponentResult> InvokeAsync(string key, string defaultText)
    {
        var block = viewContext?.IsImpersonating == true
            ? await contentBlockService.GetByKeyAsync(key)
            : await contentBlockService.EnsureAsync(key, PlainTextBlockType, defaultText, HttpContext.Request.Path.ToString());

        return Content(string.IsNullOrWhiteSpace(block?.Value) ? defaultText : block.Value);
    }
}
