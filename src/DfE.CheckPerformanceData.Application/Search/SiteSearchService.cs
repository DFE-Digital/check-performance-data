using System.Diagnostics;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.Search;

public sealed class SiteSearchService(
    IPageNodeRepository pageRepository,
    IContentBlockSearchService contentBlockSearch,
    ISearchTelemetry telemetry,
    ISearchResultCanonicaliser canonicaliser) : ISiteSearchService
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
            // Invalid requests didn't "actually run" a search, so we don't emit an event —
            // the telemetry contract is per search request that reached the backends.
            return new SiteSearchResult
            {
                CurrentQuery = term,
                ScopePath = scope,
                InvalidReason = invalidReason,
                Hits = [],
            };
        }

        var swTotal = Stopwatch.StartNew();

        // Sequential (not Task.WhenAll) — both branches share the scoped DbContext and it does
        // not tolerate concurrent operations. Each corpus is stopwatched independently so a
        // skipped corpus reports null latency (didn't run) rather than zero (ran, took 0ms).
        IReadOnlyList<PageSearchHitDto> pageHits = [];
        IReadOnlyList<FilterExclusion> pageExclusions = [];
        long? pageMs = null;
        if (query.IncludePages)
        {
            var swPages = Stopwatch.StartNew();
            (pageHits, pageExclusions) = await BuildPageHitsAsync(term, scope, query.MaxPerType);
            swPages.Stop();
            pageMs = swPages.ElapsedMilliseconds;
        }

        IReadOnlyList<ContentBlockSearchResultDto> blockHits = [];
        IReadOnlyList<FilterExclusion> blockExclusions = [];
        long? blockMs = null;
        if (query.IncludeContentBlocks)
        {
            var swBlocks = Stopwatch.StartNew();
            (blockHits, blockExclusions) = await BuildBlockHitsAsync(term, scope, query.MaxPerType);
            swBlocks.Stop();
            blockMs = swBlocks.ElapsedMilliseconds;
        }

        swTotal.Stop();

        var hitEvents = new List<SearchHitEvent>(pageHits.Count + blockHits.Count);
        foreach (var p in pageHits) hitEvents.Add(ToHitEvent(p));
        foreach (var b in blockHits) hitEvents.Add(ToHitEvent(b));

        var exclusionEvents = new List<FilterExclusion>(pageExclusions.Count + blockExclusions.Count);
        exclusionEvents.AddRange(pageExclusions);
        exclusionEvents.AddRange(blockExclusions);

        var evt = new SearchTelemetryEvent(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: DateTime.UtcNow,
            QueryRaw: query.Query ?? string.Empty,
            QueryNormalised: SearchTermNormalizer.OrJoinWhitespace(term),
            Scope: scope,
            LatencyMsTotal: swTotal.ElapsedMilliseconds,
            LatencyMsPages: pageMs,
            LatencyMsBlocks: blockMs,
            Hits: hitEvents,
            FilterExclusions: exclusionEvents);

        telemetry.RecordSearch(evt);

        // Fold pages + blocks into one canonical hit list keyed by URL. Duplicate URLs
        // (a page + N blocks on the same route, or multiple blocks on one page) collapse
        // to a single row with the MAX contributor rank as the primary sort key.
        var pageContribs = new List<PageContribution>(pageHits.Count);
        foreach (var p in pageHits)
        {
            pageContribs.Add(new PageContribution(
                Url: "/" + p.Path,
                Title: p.Title,
                SnippetHtml: p.SnippetHtml,
                Rank: p.Rank,
                PageId: p.PageId));
        }

        var blockContribs = new List<BlockContribution>(blockHits.Count);
        foreach (var b in blockHits)
        {
            blockContribs.Add(new BlockContribution(
                Url: b.Url,
                PageTitle: b.PageTitle,
                SnippetHtml: b.SnippetHtml,
                Rank: b.Rank,
                BlockKey: b.Key));
        }

        var hits = canonicaliser.Canonicalise(pageContribs, blockContribs);

        return new SiteSearchResult
        {
            CurrentQuery = term,
            ScopePath = scope,
            InvalidReason = null,
            Hits = hits,
        };
    }

    public async Task<SiteSearchPagedResult> SearchMergedPagedAsync(SiteSearchQuery query, int page, int pageSize)
    {
        var safePage = Math.Max(0, page);
        var safeSize = Math.Max(1, pageSize);

        // Ask the underlying search for a merge-window worth of hits from each corpus so
        // we can rank across them before paging. Callers pass their own MaxPerType which
        // we deliberately override — the merged window is what feeds the widget's pager.
        // Delegating to SearchAsync is also what keeps the telemetry contract to exactly
        // one emission per top-level request — do NOT emit again from this method.
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

        // raw.Hits is already URL-canonicalised and aggregate-rank ordered. Project each
        // canonical hit onto the widget's SiteSearchHit contract and paginate. The
        // canonical DTO does not carry Subtitle (pages historically surfaced it but the
        // merged widget does not) — the field is null on every item here.
        var merged = raw.Hits
            .Select(h => new SiteSearchHit(
                Title: h.Title,
                Url: h.Url,
                Subtitle: null,
                SnippetHtml: h.SnippetHtml,
                Rank: h.AggregateRank))
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

    private async Task<(IReadOnlyList<PageSearchHitDto> Hits, IReadOnlyList<FilterExclusion> Exclusions)>
        BuildPageHitsAsync(string term, string? scope, int max)
    {
        var raw = await pageRepository.SearchPagesAsync(term, scope, max);

        var hits = new List<PageSearchHitDto>(raw.Count);
        var exclusions = new List<FilterExclusion>();
        foreach (var r in raw)
        {
            if (r.ExcludedBy is not null)
            {
                exclusions.Add(new FilterExclusion("page", r.ExcludedBy, r.Path));
                continue;
            }

            hits.Add(new PageSearchHitDto
            {
                PageId = r.PageId,
                Path = r.Path,
                Title = r.Title,
                Subtitle = r.Subtitle,
                SnippetHtml = BuildSnippet(r.BodyPlainText, term, r.Title, r.Subtitle),
                Rank = r.Rank,
                RankKeywords = r.RankKeywords,
                RankTitle = r.RankTitle,
                RankSubtitle = r.RankSubtitle,
                RankBody = r.RankBody,
            });
        }

        return (hits, exclusions);
    }

    private async Task<(IReadOnlyList<ContentBlockSearchResultDto> Hits, IReadOnlyList<FilterExclusion> Exclusions)>
        BuildBlockHitsAsync(string term, string? scope, int max)
    {
        var outcome = await contentBlockSearch.SearchAsync(term, max);
        if (scope is null) return (outcome.Hits, outcome.Exclusions);

        var scopePrefix = "/" + scope;
        var scopeSubtree = scopePrefix + "/";
        var scoped = outcome.Hits
            .Where(h => h.Url.Equals(scopePrefix, StringComparison.OrdinalIgnoreCase)
                     || h.Url.StartsWith(scopeSubtree, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (scoped, outcome.Exclusions);
    }

    // Per-corpus asymmetry lives here rather than at the caller so the assembly site
    // reads as a flat concat. Page hits carry Title/Subtitle/Body ranks; RankValue is
    // null (blocks-only field). Block hits carry Keywords/Value ranks; the three
    // title/subtitle/body ranks are null (pages-only fields).
    private static SearchHitEvent ToHitEvent(PageSearchHitDto p) => new(
        Corpus: "page",
        RowId: p.PageId.ToString(),
        Url: "/" + p.Path,
        Title: p.Title,
        RankTotal: p.Rank,
        RankKeywords: p.RankKeywords,
        RankTitle: p.RankTitle,
        RankSubtitle: p.RankSubtitle,
        RankBody: p.RankBody,
        RankValue: null);

    private static SearchHitEvent ToHitEvent(ContentBlockSearchResultDto b) => new(
        Corpus: "block",
        RowId: b.Key,
        Url: b.Url,
        Title: b.PageTitle,
        RankTotal: b.Rank,
        RankKeywords: b.RankKeywords,
        RankTitle: null,
        RankSubtitle: null,
        RankBody: null,
        RankValue: b.RankValue);

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
