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
        var path = HttpContext.Request.Path.ToString();

        // Auto-provision on first render: if no block exists for this key yet, create one with the
        // template's default HTML so it shows up under the correct page in /admin/content-blocks.
        // Subsequent renders update LastSeenPath if the page moved.
        var block = await contentBlockService.EnsureAsync(key, "Content", defaultHtml, path);

        var model = new EditableContentViewModel
        {
            Key = key,
            Value = block.Value,
            ValueHtml = block.ValueHtml ?? defaultHtml,
            IsEditing = isEditing,
            HasSavedContent = true,
            ReturnUrl = $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}"
        };

        return View(model);
    }
}
