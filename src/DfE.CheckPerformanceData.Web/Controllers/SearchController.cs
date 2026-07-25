using System.Net;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Unified site-search endpoint. Reachable at:
//   /search              — global search (all pages + all content blocks)
//   /{scope}/search      — scoped search, where {scope} is a page path prefix (e.g. "guidance").
// Everything lands on the same view so results are always in one place, regardless of which
// search widget or PageNav search box submitted the query.
//
// ISettingService supplies the shared CmsPageLength admin knob when the request does not pin
// its own ?pageSize. The controller mirrors the content-page results widget so both surfaces
// obey the same knob and cannot drift.
[AllowAnonymous]
public sealed class SearchController(
    ISiteSearchService searchService,
    ISettingService settings) : Controller
{
    [HttpGet("/search")]
    public Task<IActionResult> Index(
        string? q,
        string? scope,
        bool? includePages,
        bool? includeContentBlocks,
        int? page,
        int? pageSize,
        CancellationToken ct = default)
        => RenderAsync(q, scope, includePages, includeContentBlocks, page, pageSize, ct);

    private async Task<IActionResult> RenderAsync(
        string? q,
        string? scope,
        bool? includePages,
        bool? includeContentBlocks,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        // Query-length trim. Whitespace-first so trailing spaces on a 101-char paste do not
        // count against the cap; then a hard leading-100-chars slice. Silent — no user hint.
        if (!string.IsNullOrEmpty(q))
        {
            q = q.Trim();
            if (q.Length > 100)
            {
                q = q[..100];
            }
        }

        // PageSize resolution: query-string overrides the shared admin setting; the setting's
        // default (20) covers the unset case, but a defence-in-depth guard mirrors the widget
        // in case a bad value slips past the settings pipeline. Clamp comes last so an admin
        // knob of 60 or a ?pageSize=999 both land inside [10, 50].
        var effectivePageSize = pageSize ?? await settings.GetIntAsync(SettingKeys.CmsPageLength);
        if (effectivePageSize < 1)
        {
            effectivePageSize = 20;
        }
        effectivePageSize = Math.Clamp(effectivePageSize, 10, 50);

        // Page is one-indexed at the URL boundary (?page=1 = first page); the total-based
        // upper clamp cannot land until the service reports TotalPages.
        var oneIndexedPage = Math.Max(1, page ?? 1);

        var includePagesResolved = includePages ?? true;
        var includeContentBlocksResolved = includeContentBlocks ?? true;

        var query = new SiteSearchQuery(
            Query: q,
            ScopePath: scope,
            IncludePages: includePagesResolved,
            IncludeContentBlocks: includeContentBlocksResolved,
            Page: oneIndexedPage,
            PageSize: effectivePageSize);

        var result = await searchService.SearchAsync(query);

        if (result.InvalidReason == SearchInvalidReason.DataStoreUnavailable)
        {
            // No stack trace, no exception detail — the view renders GDS copy only. The
            // retry link keeps the user's typed query so a refresh does not lose it.
            Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return View("Unavailable", new SiteSearchViewModel
            {
                Query = q ?? string.Empty,
                Scope = scope,
                InvalidReason = SearchInvalidReason.DataStoreUnavailable,
                Hits = Array.Empty<CanonicalSearchHit>(),
                IncludePages = includePagesResolved,
                IncludeContentBlocks = includeContentBlocksResolved,
                Page = oneIndexedPage,
                PageSize = effectivePageSize,
                TotalCount = 0,
            });
        }

        // Page over-clamp: if the user asked for page 12 of a corpus with 3 pages, re-issue
        // at the last valid page. Costs one extra call in the pathological case; keeps the
        // shape trivial. A canonicalised corpus with zero hits (or an invalid query) leaves
        // TotalPages at 0 — the guard below ensures we do not re-issue in that case.
        if (result.TotalPages > 0 && oneIndexedPage > result.TotalPages)
        {
            oneIndexedPage = result.TotalPages;
            query = query with { Page = oneIndexedPage };
            result = await searchService.SearchAsync(query);
        }

        return View("Index", new SiteSearchViewModel
        {
            Query = result.CurrentQuery,
            Scope = result.ScopePath,
            InvalidReason = result.InvalidReason,
            Hits = result.Hits,
            IncludePages = includePagesResolved,
            IncludeContentBlocks = includeContentBlocksResolved,
            Page = oneIndexedPage,
            PageSize = effectivePageSize,
            TotalCount = result.TotalCount,
        });
    }
}
