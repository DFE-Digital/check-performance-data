using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// The open editor offers the media library only to people with the media section, which
// gates the picker and its upload.
public sealed class EditableContentViewComponent(IContentBlockService contentBlockService,
    DfE.CheckPerformanceData.Application.Impersonation.IEstablishmentViewContext? viewContext = null,
    DfE.CheckPerformanceData.Application.Admin.IAdminAccessPolicy? accessPolicy = null) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(
        string key,
        string defaultHtml)
    {
        var impersonating = viewContext?.IsImpersonating == true;
        ViewData["Impersonating"] = impersonating;
        var isEditing = !impersonating && HttpContext.Request.Query["edit"].ToString() == key;
        var path = HttpContext.Request.Path.ToString();

        // Auto-provision on first render: if no block exists for this key yet, create one with the
        // template's default HTML so it shows up under the correct page in /admin/content-blocks.
        // Subsequent renders update LastSeenPath if the page moved.
        var block = impersonating
            ? await contentBlockService.GetByKeyAsync(key) ?? new ContentBlockDto { Key = key, Value = defaultHtml }
            : await contentBlockService.EnsureAsync(key, "Content", defaultHtml, path);

        var model = new EditableContentViewModel
        {
            Key = key,
            Value = block.Value,
            ValueHtml = block.ValueHtml ?? defaultHtml,
            IsEditing = isEditing,
            HasSavedContent = true,
            ReturnUrl = $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}",
            CanUseMediaLibrary = isEditing && accessPolicy is not null
                && await accessPolicy.CanAccessAsync(UserClaimsPrincipal, DfE.CheckPerformanceData.Web.Admin.Nav.AdminNavKeys.Media),
        };

        return View(model);
    }
}
