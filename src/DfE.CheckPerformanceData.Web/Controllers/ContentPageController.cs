using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Editor for database-backed content pages. Every action is editor-role gated and every mutation is a
// POST guarded by antiforgery, then redirects back to the editor (post/redirect/get). The public
// rendering lives on GuidanceController; this controller only edits drafts and publishes them.
[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class ContentPageController(IContentPageService pages, IContentPageEditor editor) : Controller
{
    [HttpGet("/admin/content-pages")]
    public async Task<IActionResult> Index() => View(await pages.GetAllAsync());

    [HttpGet("/guidance/new")]
    public IActionResult New() => View(new NewContentPageModel());

    [HttpPost("/content-page/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewContentPageModel model)
    {
        if (!ModelState.IsValid)
            return View("New", model);

        // Slug defaults to a slugified title; either way it is normalised so it is URL- and route-safe.
        var slug = WikiSlug.Generate(string.IsNullOrWhiteSpace(model.Slug) ? model.Title : model.Slug);
        if (slug.Length == 0)
        {
            ModelState.AddModelError(nameof(model.Title), "Enter a title or slug.");
            return View("New", model);
        }

        await pages.CreateAsync(slug, model.Title, model.Caption, model.Layout);
        return RedirectToEdit(slug);
    }

    [HttpGet("/guidance/{slug}/edit")]
    public async Task<IActionResult> Edit(string slug)
    {
        var page = await pages.GetBySlugAsync(slug);
        if (page is null)
            return NotFound();

        return View(new ContentPageEditViewModel
        {
            Slug = page.Slug,
            Title = page.Title,
            Layout = page.Layout,
            Content = ContentPageJson.Deserialize(page.DraftContent) ?? [],
            PublishedVersionNumber = page.PublishedVersionNumber
        });
    }

    [HttpPost("/content-page/{slug}/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string slug, string path, string? widgetType, string? regionLayout)
    {
        var steps = TreePath.Parse(path);

        if (!string.IsNullOrEmpty(widgetType))
            await editor.AddWidgetAsync(slug, steps, widgetType);
        else if (Enum.TryParse<RegionLayout>(regionLayout, out var layout))
            await editor.AddRegionAsync(slug, steps, layout);

        return RedirectToEdit(slug);
    }

    [HttpPost("/content-page/{slug}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string slug, string path, string direction)
    {
        var steps = TreePath.Parse(path);

        if (direction == "up")
            await editor.MoveUpAsync(slug, steps);
        else
            await editor.MoveDownAsync(slug, steps);

        return RedirectToEdit(slug);
    }

    [HttpPost("/content-page/{slug}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string slug, string path)
    {
        await editor.DeleteAsync(slug, TreePath.Parse(path));
        return RedirectToEdit(slug);
    }

    [HttpPost("/content-page/{slug}/widget")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWidget(string slug, string path, string type, [FromForm] Dictionary<string, string?>? props)
    {
        var built = WidgetPropsBuilder.Build(type, props ?? new Dictionary<string, string?>());
        await editor.UpdateWidgetAsync(slug, TreePath.Parse(path), built);
        return RedirectToEdit(slug);
    }

    [HttpPost("/content-page/{slug}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string slug)
    {
        await pages.PublishAsync(slug);
        return Redirect($"/guidance/{slug}");
    }

    private IActionResult RedirectToEdit(string slug) => Redirect($"/guidance/{slug}/edit");
}
