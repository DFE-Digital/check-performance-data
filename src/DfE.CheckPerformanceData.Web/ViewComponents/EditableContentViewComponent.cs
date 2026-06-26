using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

public sealed class EditableContentViewComponent(IContentBlockService contentBlockService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(
        string key,
        string defaultHtml)
    {
        var isEditing = HttpContext.Request.Query["edit"].ToString() == key;
        var block = await contentBlockService.GetByKeyAsync(key);

        // Record which page this block currently sits on (path only, no query). Writes only when
        // the path changed, so repeat views of an unchanged block don't touch the database.
        var path = HttpContext.Request.Path.ToString();
        if (block is not null && block.LastSeenPath != path)
            await contentBlockService.RecordLastSeenAsync(key, path);

        var model = new EditableContentViewModel
        {
            Key = key,
            Value = block?.Value ?? defaultHtml,
            ValueHtml = block?.ValueHtml ?? defaultHtml,
            IsEditing = isEditing,
            HasSavedContent = block != null,
            ReturnUrl = $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}"
        };

        return View(model);
    }
}
