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
        var path = HttpContext.Request.Path.ToString();

        // Auto-provision on first render so the block appears in the admin tree with the
        // template's default text as its initial value.
        var block = await contentBlockService.EnsureAsync(key, "Title", defaultText, path);

        var model = new EditableTitleViewModel
        {
            Key = key,
            Value = block.Value,
            HeadingLevel = headingLevel,
            CssClass = cssClass,
            IsEditing = isEditing,
            HasSavedContent = true,
            ReturnUrl = $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}"
        };

        return View(model);
    }
}
