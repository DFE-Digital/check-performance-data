using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

public sealed class EditableTitleViewComponent(IContentBlockService contentBlockService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(
        string key,
        string defaultText,
        string headingLevel = "h1",
        string cssClass = "govuk-heading-xl")
    {
        var isEditing = HttpContext.Request.Query["edit"].ToString() == key;
        var block = await contentBlockService.GetByKeyAsync(key);

        var model = new EditableTitleViewModel
        {
            Key = key,
            Value = block?.Value ?? defaultText,
            HeadingLevel = headingLevel,
            CssClass = cssClass,
            IsEditing = isEditing,
            HasSavedContent = block != null,
            ReturnUrl = $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}"
        };

        return View(model);
    }
}
