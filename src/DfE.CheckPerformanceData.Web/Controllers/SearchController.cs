using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Unified site-search endpoint. Reachable at:
//   /search              — global search (all pages + all content blocks)
//   /{scope}/search      — scoped search, where {scope} is a page path prefix (e.g. "guidance").
// Everything lands on the same view so results are always in one place, regardless of which
// search widget or PageNav search box submitted the query.
[AllowAnonymous]
public sealed class SearchController(ISiteSearchService searchService) : Controller
{
    [HttpGet("/search")]
    public Task<IActionResult> Index(string? q, string? scope, bool? includePages, bool? includeContentBlocks)
        => RenderAsync(q, scope, includePages, includeContentBlocks);

    private async Task<IActionResult> RenderAsync(string? q, string? scope, bool? includePages, bool? includeContentBlocks)
    {
        var result = await searchService.SearchAsync(new SiteSearchQuery(
            q,
            ScopePath: scope,
            IncludePages: includePages ?? true,
            IncludeContentBlocks: includeContentBlocks ?? true));

        return View("Index", new SiteSearchViewModel
        {
            Query = result.CurrentQuery,
            Scope = result.ScopePath,
            InvalidReason = result.InvalidReason,
            Hits = result.Hits,
            IncludePages = includePages ?? true,
            IncludeContentBlocks = includeContentBlocks ?? true,
        });
    }
}
