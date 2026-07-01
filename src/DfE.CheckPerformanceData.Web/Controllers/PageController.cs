using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.PageTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Catch-all resolver: any URL not claimed by a real app route falls through here.
// Order = int.MaxValue on the HttpGet attribute guarantees this endpoint is always
// evaluated last, so it can never shadow a real route.
[AllowAnonymous]
public sealed class PageController(IPageNodeService pageNodes) : Controller
{
    [HttpGet("/{*path}", Order = int.MaxValue)]
    public async Task<IActionResult> Show(string path)
    {
        var result = await pageNodes.GetLivePageAsync(path, DateTime.UtcNow);
        if (result is null)
            return NotFound();

        return result.Node.PageType switch
        {
            "content" => BuildContentView(result),
            "wiki" => View("Wiki", new RenderedPageViewModel
            {
                Title    = result.Node.Title,
                PageType = "wiki",
                WikiHtml = result.Version.Content
            }),
            "folder" => View("Folder", new RenderedPageViewModel
            {
                Title    = result.Node.Title,
                PageType = "folder"
            }),
            _ => NotFound()
        };
    }

    // Deserialises the content tree and builds the in-page heading nav, mirroring
    // GuidanceController.Show which does the same from a ContentPage payload.
    private IActionResult BuildContentView(LivePageResult result)
    {
        var tree = ContentPageJson.Deserialize(result.Version.Content) ?? [];
        return View("Content", new RenderedPageViewModel
        {
            Title    = result.Node.Title,
            PageType = "content",
            Content  = tree,
            Nav      = ContentNavBuilder.Build(tree)
        });
    }
}
