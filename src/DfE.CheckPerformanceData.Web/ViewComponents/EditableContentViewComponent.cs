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
