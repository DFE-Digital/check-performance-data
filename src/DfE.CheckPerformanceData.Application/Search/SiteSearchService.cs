using System.Net;
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
        return raw
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

    // Build a 200-char window around the first case-insensitive hit in the body; fall back to
    // the title/subtitle if the body has no direct match. Everything is HTML-encoded and the
    // matched term wrapped in <mark> — the only markup that reaches the results page.
    private static string BuildSnippet(string body, string term, string title, string? subtitle)
    {
        var source = FirstMatchSource(body, term)
            ?? FirstMatchSource(subtitle ?? string.Empty, term)
            ?? FirstMatchSource(title, term)
            ?? body;

        var idx = source.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            var head = source.Length <= 200 ? source : source[..200] + "…";
            return WebUtility.HtmlEncode(head);
        }

        var start = Math.Max(0, idx - 60);
        var length = Math.Min(source.Length - start, 200);
        var window = source.Substring(start, length);
        var wi = window.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        var before = WebUtility.HtmlEncode(window[..wi]);
        var match = WebUtility.HtmlEncode(window.Substring(wi, term.Length));
        var after = WebUtility.HtmlEncode(window[(wi + term.Length)..]);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = start + length < source.Length ? "…" : string.Empty;
        return $"{prefix}{before}<mark>{match}</mark>{after}{suffix}";
    }

    private static string? FirstMatchSource(string text, string term)
        => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ? text : null;
}
