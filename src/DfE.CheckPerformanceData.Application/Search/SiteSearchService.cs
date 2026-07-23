using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.Search;

public sealed class SiteSearchService(
    IPageNodeRepository pageRepository,
    IContentBlockSearchService contentBlockSearch) : ISiteSearchService
{
    private const int MinTermLength = 2;
    // Upper bound on how many hits per corpus SearchMergedPagedAsync fetches to feed its
    // in-memory merge. For the current CMS corpus (<200 pages, <500 blocks) this covers
    // any conceivable term. For substantially larger deployments the merge should move to
    // a DB-level UNION with LIMIT/OFFSET rather than growing this cap.
    private const int MergedFetchCap = 500;

    public async Task<SiteSearchResult> SearchAsync(SiteSearchQuery query)
    {
        var term = (query.Query ?? string.Empty).Trim();
        var scope = string.IsNullOrWhiteSpace(query.ScopePath) ? null : query.ScopePath.Trim().Trim('/');

        SearchInvalidReason? invalidReason = term.Length switch
        {
            0 => SearchInvalidReason.EmptyQuery,
            < MinTermLength => SearchInvalidReason.BelowMinimumLength,
            _ => null,
        };

        if (invalidReason is not null)
        {
            return new SiteSearchResult
            {
                CurrentQuery = term,
                ScopePath = scope,
                InvalidReason = invalidReason,
                PageHits = [],
                ContentBlockHits = [],
            };
        }

        // Sequential (not Task.WhenAll) — both branches share the scoped DbContext and it does
        // not tolerate concurrent operations.
        var pageHits = query.IncludePages
            ? await BuildPageHitsAsync(term, scope, query.MaxPerType)
            : (IReadOnlyList<PageSearchHitDto>)[];

        var blockHits = query.IncludeContentBlocks
            ? await BuildBlockHitsAsync(term, scope, query.MaxPerType)
            : (IReadOnlyList<ContentBlockSearchResultDto>)[];

        return new SiteSearchResult
        {
            CurrentQuery = term,
            ScopePath = scope,
            InvalidReason = null,
            PageHits = pageHits,
            ContentBlockHits = blockHits,
        };
    }

    public async Task<SiteSearchPagedResult> SearchMergedPagedAsync(SiteSearchQuery query, int page, int pageSize)
    {
        var safePage = Math.Max(0, page);
        var safeSize = Math.Max(1, pageSize);

        // Ask the underlying search for a merge-window worth of hits from each corpus so
        // we can rank across them before paging. Callers pass their own MaxPerType which
        // we deliberately override — the merged window is what feeds the widget's pager.
        var fetch = query with { MaxPerType = MergedFetchCap };
        var raw = await SearchAsync(fetch);

        if (raw.InvalidReason is not null)
        {
            return new SiteSearchPagedResult
            {
                CurrentQuery = raw.CurrentQuery,
                ScopePath = raw.ScopePath,
                InvalidReason = raw.InvalidReason,
                Items = [],
                TotalCount = 0,
                Page = safePage,
                PageSize = safeSize,
            };
        }

        var merged = raw.PageHits
            .Select(p => new SiteSearchHit(
                Title: p.Title,
                Url: "/" + p.Path,
                Subtitle: p.Subtitle,
                SnippetHtml: p.SnippetHtml,
                Rank: p.Rank))
            .Concat(raw.ContentBlockHits.Select(b => new SiteSearchHit(
                Title: b.PageTitle,
                Url: b.Url,
                Subtitle: null,
                SnippetHtml: b.SnippetHtml,
                Rank: b.Rank)))
            .OrderByDescending(h => h.Rank)
            .ToList();

        var slice = merged
            .Skip(safePage * safeSize)
            .Take(safeSize)
            .ToList();

        return new SiteSearchPagedResult
        {
            CurrentQuery = raw.CurrentQuery,
            ScopePath = raw.ScopePath,
            InvalidReason = null,
            Items = slice,
            TotalCount = merged.Count,
            Page = safePage,
            PageSize = safeSize,
        };
    }

    private async Task<IReadOnlyList<PageSearchHitDto>> BuildPageHitsAsync(string term, string? scope, int max)
    {
        var raw = await pageRepository.SearchPagesAsync(term, scope, max);
        // Transitional: excluded rows now arrive from the widened repository projection —
        // the next wave threads them into telemetry via a per-row exclusion event. This
        // defensive filter preserves the shipped hit set until then.
        return raw
            .Where(r => r.ExcludedBy == null)
            .Select(r => new PageSearchHitDto
            {
                PageId = r.PageId,
                Path = r.Path,
                Title = r.Title,
                Subtitle = r.Subtitle,
                SnippetHtml = BuildSnippet(r.BodyPlainText, term, r.Title, r.Subtitle),
                Rank = r.Rank,
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ContentBlockSearchResultDto>> BuildBlockHitsAsync(string term, string? scope, int max)
    {
        var hits = await contentBlockSearch.SearchAsync(term, max);
        if (scope is null) return hits;

        var scopePrefix = "/" + scope;
        var scopeSubtree = scopePrefix + "/";
        return hits
            .Where(h => h.Url.Equals(scopePrefix, StringComparison.OrdinalIgnoreCase)
                     || h.Url.StartsWith(scopeSubtree, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Pick a source with a direct case-insensitive match, preferring body, then subtitle,
    // then title, falling back to body if none contain the term. Windowing + HTML-encoding
    // + <mark>-wrap logic lives on the shared SearchSnippet helper.
    private static string BuildSnippet(string body, string term, string title, string? subtitle)
    {
        var source = FirstMatchSource(body, term)
            ?? FirstMatchSource(subtitle ?? string.Empty, term)
            ?? FirstMatchSource(title, term)
            ?? body;

        return SearchSnippet.BuildWindow(source, term);
    }

    private static string? FirstMatchSource(string text, string term)
        => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ? text : null;
}
