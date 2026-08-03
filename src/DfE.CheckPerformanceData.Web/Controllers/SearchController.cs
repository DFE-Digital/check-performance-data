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
        int? page = null,
        int? pageSize = null,
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

        // PageSize resolution.
        //   ?pageSize on the URL is untrusted per-request input and clamped to [10, 50] as
        //   an anti-abuse guard so a pathological value cannot inflate the per-corpus fetch.
        //   The admin CmsPageLength setting is trusted (same convention as PageTreeAdmin,
        //   QueueAdmin, AppLogs, and Observability) — honoured as-is with a floor of 1 so a
        //   0 / negative stored value cannot divide-by-zero the paging math.
        int effectivePageSize;
        if (pageSize is int urlPageSize)
        {
            effectivePageSize = Math.Clamp(urlPageSize, 10, 50);
        }
        else
        {
            var adminPageSize = await settings.GetIntAsync(SettingKeys.CmsPageLength);
            effectivePageSize = adminPageSize > 0 ? adminPageSize : 20;
        }

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

        // Page over-clamp is handled inside the search service, which pages an already-
        // materialised list and so can land on the last valid page without a second pass.
        // Re-issuing from here would run both corpus searches again and record a second
        // telemetry event for the same user request. Read the page back off the result
        // (zero-indexed internally, one-indexed at the URL boundary) so the view reflects
        // wherever the service actually landed.
        return View("Index", new SiteSearchViewModel
        {
            Query = result.CurrentQuery,
            Scope = result.ScopePath,
            InvalidReason = result.InvalidReason,
            Hits = result.Hits,
            IncludePages = includePagesResolved,
            IncludeContentBlocks = includeContentBlocksResolved,
            Page = result.Page + 1,
            PageSize = effectivePageSize,
            TotalCount = result.TotalCount,
        });
    }
}
