using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class GuidanceController(IContentPageService contentPages) : Controller
{
    [HttpGet("/guidance")]
    public IActionResult Index() => View();

    [HttpGet("/guidance/2026-ks4-june-checking-exercise")]
    public IActionResult Ks4June2026() => View(GuidancePage.Ks4June2026);

    // Renders a database-backed content page from its latest published version. The two literal routes
    // above are more specific and still win; this catch-all serves every other guidance slug.
    [HttpGet("/guidance/{slug}")]
    public async Task<IActionResult> Show(string slug)
    {
        var page = await contentPages.GetBySlugAsync(slug);
        if (page?.PublishedVersionNumber is null)
            return NotFound();

        var published = await contentPages.GetPublishedContentAsync(slug);
        var tree = ContentPageJson.Deserialize(published ?? "[]") ?? [];

        return View(new ContentPageViewModel
        {
            Slug = page.Slug,
            Title = page.Title,
            Caption = page.Caption,
            Layout = page.Layout,
            Content = tree,
            Nav = ContentNavBuilder.Build(tree)
        });
    }
}
