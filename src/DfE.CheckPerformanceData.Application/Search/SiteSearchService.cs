using System.Data.Common;
using System.Diagnostics;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Search;

public sealed class SiteSearchService(
    IPageNodeRepository pageRepository,
    IContentBlockSearchService contentBlockSearch,
    ISearchTelemetry telemetry,
    ISearchResultCanonicaliser canonicaliser,
    ILogger<SiteSearchService> logger) : ISiteSearchService
{
    private const int MinTermLength = 2;

    public async Task<SiteSearchPagedResult> SearchAsync(SiteSearchQuery query)
    {
        // Strip embedded null bytes before anything else touches the term. Postgres text
        // parameters cannot carry U+0000 (the wire protocol rejects them outright), so a
        // stray null-byte in user input would otherwise cascade into an ArgumentException
        // from Npgsql that ultimately surfaces as DataStoreUnavailable via the retry
        // strategy. Strip them so a null-byte-bearing input degrades to a clean "search
        // over the surviving characters" rather than a spurious unavailable response.
        var rawTerm = (query.Query ?? string.Empty);
        if (rawTerm.IndexOf('\0') >= 0) rawTerm = rawTerm.Replace("\0", string.Empty);
        var term = rawTerm.Trim();
        var scope = string.IsNullOrWhiteSpace(query.ScopePath) ? null : query.ScopePath.Trim().Trim('/');
        var pageOneIx = Math.Max(1, query.Page);
        var pageSize = Math.Max(1, query.PageSize);

        try
        {
            var (primaryResult, primaryEvent) = await SearchOnceAsync(query, term, scope, pageOneIx, pageSize);

            // Zero-result queries that contain a hyphen and are not operator-only trigger a
            // single retry with hyphens replaced by spaces — closes the URL-slug matching gap
            // where websearch_to_tsquery emits a compound-lexeme phrase clause that misses the
            // per-word tsvector. Zero cost when the primary already matched; only kicks in when
            // the user would otherwise see the zero-results screen. Operator-bearing inputs are
            // exempt so an explicit -negation or "phrase" isn't second-guessed.
            var shouldFallback = primaryResult.Hits.Count == 0
                                 && !string.IsNullOrEmpty(term)
                                 && term.Contains('-')
                                 && !SearchTermNormalizer.ContainsWebSearchOperator(term);

            if (shouldFallback)
            {
                var replacedTerm = term.Replace('-', ' ');
                var (fallbackResult, fallbackEvent) = await SearchOnceAsync(query, replacedTerm, scope, pageOneIx, pageSize);

                if (fallbackResult.Hits.Count > 0)
                {
                    if (fallbackEvent is not null)
                    {
                        // Single telemetry emission per user-facing request: reflects what the
                        // user actually experienced (the successful fallback), not the primary.
                        telemetry.RecordSearch(fallbackEvent);
                    }

                    logger.LogInformation(
                        "Search hyphen-fallback rescued SearchId={SearchId} QueryRaw={QueryRaw} QueryHyphenReplaced={QueryHyphenReplaced} ResultsHits={ResultsHits}",
                        fallbackEvent?.SearchId ?? Guid.Empty,
                        term,
                        replacedTerm,
                        fallbackResult.Hits.Count);

                    return fallbackResult;
                }

                // Fallback also returned zero — fall through to the primary-emission path so
                // the zero-result Warn from the sink still fires on the user's original query.
            }

            if (primaryEvent is not null)
            {
                // Single telemetry emission per user-facing request that reached the backends.
                telemetry.RecordSearch(primaryEvent);
            }

            return primaryResult;
        }
        // Catch DbException at the Application boundary (fully qualified for grep-audit).
        // The persistence layer registers PortalDbContext with EnableRetryOnFailure, which
        // installs NpgsqlRetryingExecutionStrategy. When retries are exhausted the strategy
        // wraps the terminal DbException in RetryLimitExceededException; catching only the
        // bare DbException here would miss the retry-strategy path and let the wrapper
        // propagate. Catch both — the untangled DbException for the no-retry case (e.g.
        // tests that opt out of retry), and RetryLimitExceededException whose InnerException
        // carries the same DbException for the production retry path. Either way we return
        // DataStoreUnavailable on the paged result rather than throw. Callers pick their
        // own UX; the telemetry sink stays honest about "actually reached the DB and
        // completed" (the DB-unavailable branch emits zero events).
        catch (Exception ex) when (ex is System.Data.Common.DbException
                                   || (ex is RetryLimitExceededException && ex.InnerException is System.Data.Common.DbException))
        {
            logger.LogWarning(
                ex,
                "Search failed data store unavailable QueryRaw={QueryRaw} QueryNormalised={QueryNormalised} Scope={Scope}",
                term,
                SearchTermNormalizer.OrJoinWhitespace(term),
                scope ?? "(none)");

            return new SiteSearchPagedResult
            {
                CurrentQuery = term,
                ScopePath = scope,
                InvalidReason = SearchInvalidReason.DataStoreUnavailable,
                Hits = Array.Empty<CanonicalSearchHit>(),
                TotalCount = 0,
                Page = pageOneIx - 1,
                PageSize = pageSize,
            };
        }
    }

    // Runs one canonicalised search pass and builds (but does not emit) the telemetry event.
    // The public SearchAsync decides when to call telemetry.RecordSearch so a DB-unavailable
    // branch produces zero events and a future hyphen-fallback re-invocation still emits
    // exactly one event for the user-facing request.
    private async Task<(SiteSearchPagedResult Result, SearchTelemetryEvent? Event)> SearchOnceAsync(
        SiteSearchQuery query,
        string term,
        string? scope,
        int oneIndexedPage,
        int pageSize)
    {
        var safePage = Math.Max(1, oneIndexedPage);
        var safeSize = Math.Max(1, pageSize);

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
            return (new SiteSearchPagedResult
            {
                CurrentQuery = term,
                ScopePath = scope,
                InvalidReason = invalidReason,
                Hits = Array.Empty<CanonicalSearchHit>(),
                TotalCount = 0,
                Page = safePage - 1,
                PageSize = safeSize,
            }, null);
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

        if (pageHits.Count == query.MaxPerType || blockHits.Count == query.MaxPerType)
        {
            // Either corpus fetched exactly the cap — the merged view may be silently
            // truncated. Warn so ops can spot corpus growth pushing past the ceiling.
            logger.LogWarning(
                "Search merge cap reached QueryRaw={QueryRaw} PageHitsCount={PageHitsCount} BlockHitsCount={BlockHitsCount} MergedFetchCap={MergedFetchCap}",
                term,
                pageHits.Count,
                blockHits.Count,
                query.MaxPerType);
        }

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

        var canonicalised = canonicaliser.Canonicalise(pageContribs, blockContribs);

        // Per-canonical telemetry: one SearchHitEvent per URL the user actually sees. The
        // winning contributor for each canonical row supplies the per-field rank breakdown
        // — page contributors bring RankKeywords/Title/Subtitle/Body (RankValue null);
        // block contributors bring RankKeywords/RankValue (the three page-only ranks null).
        // Winner is the highest-raw-rank contributor for the URL; ties go to the page
        // (matches the canonicaliser's snippet-selection tie-break).
        var pageByUrl = new Dictionary<string, PageSearchHitDto>(pageHits.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in pageHits)
        {
            pageByUrl["/" + p.Path] = p;
        }

        var blocksByUrl = new Dictionary<string, List<ContentBlockSearchResultDto>>(blockHits.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var b in blockHits)
        {
            if (!blocksByUrl.TryGetValue(b.Url, out var bucket))
            {
                bucket = new List<ContentBlockSearchResultDto>();
                blocksByUrl[b.Url] = bucket;
            }
            bucket.Add(b);
        }

        var hitEvents = new List<SearchHitEvent>(canonicalised.Count);
        foreach (var h in canonicalised)
        {
            hitEvents.Add(BuildHitEvent(h, pageByUrl, blocksByUrl));
        }

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

        // In-memory pagination on the canonicalised URL list. TotalCount is the accurate
        // post-canonicalisation count (up to the per-corpus fetch cap); Skip/Take carves
        // the requested page out of that ordered list.
        //
        // Over-run is clamped right here rather than by the caller. The list is already
        // materialised, so the last valid page costs nothing extra — whereas a caller that
        // notices the over-run can only fix it by calling SearchAsync again, which re-runs
        // both corpus searches and emits a SECOND telemetry event. That made one user
        // request record two search rows and double-count itself across the volume,
        // unique-query, latency and per-result charts. An empty corpus has no page to clamp
        // to, so the requested page passes through and the view renders its empty state.
        var totalPages = canonicalised.Count > 0
            ? (canonicalised.Count + safeSize - 1) / safeSize
            : 0;
        if (totalPages > 0 && safePage > totalPages)
        {
            safePage = totalPages;
        }

        var pagedHits = canonicalised
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .ToArray();

        var result = new SiteSearchPagedResult
        {
            CurrentQuery = term,
            ScopePath = scope,
            InvalidReason = null,
            Hits = pagedHits,
            TotalCount = canonicalised.Count,
            Page = safePage - 1,
            PageSize = safeSize,
        };

        return (result, evt);
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

    // Build the per-canonical SearchHitEvent for a single URL row. Winning contributor is
    // the one with the highest raw ts_rank on that URL; ties resolve to the page (matches
    // the snippet-selection tie-break inside SearchResultCanonicaliser). Winner's per-field
    // ranks flow onto the event; the corpus-orthogonal fields (RankValue on a page winner,
    // RankTitle/Subtitle/Body on a block winner) stay null by design so the downstream sink
    // can tell "not applicable" apart from "unknown".
    private static SearchHitEvent BuildHitEvent(
        CanonicalSearchHit hit,
        Dictionary<string, PageSearchHitDto> pageByUrl,
        Dictionary<string, List<ContentBlockSearchResultDto>> blocksByUrl)
    {
        pageByUrl.TryGetValue(hit.Url, out var page);
        blocksByUrl.TryGetValue(hit.Url, out var blocks);

        // Determine the highest-ranked block contributor for this URL (if any).
        ContentBlockSearchResultDto? topBlock = null;
        if (blocks is not null)
        {
            foreach (var b in blocks)
            {
                if (topBlock is null || b.Rank > topBlock.Rank) topBlock = b;
            }
        }

        var pageWins = page is not null && (topBlock is null || page.Rank >= topBlock.Rank);

        if (pageWins && page is not null)
        {
            return new SearchHitEvent(
                Url: hit.Url,
                Title: hit.Title,
                RankTotal: hit.AggregateRank,
                PageContributed: hit.PageContributorCount > 0,
                BlockContributorCount: hit.BlockContributorCount,
                ContributingBlockKeys: hit.ContributingBlockKeys,
                RankKeywords: page.RankKeywords,
                RankTitle: page.RankTitle,
                RankSubtitle: page.RankSubtitle,
                RankBody: page.RankBody,
                RankValue: null);
        }

        // Block-sourced winner (or page absent). topBlock is non-null here because a row
        // reaches the canonicaliser only if it has at least one contributor.
        return new SearchHitEvent(
            Url: hit.Url,
            Title: hit.Title,
            RankTotal: hit.AggregateRank,
            PageContributed: hit.PageContributorCount > 0,
            BlockContributorCount: hit.BlockContributorCount,
            ContributingBlockKeys: hit.ContributingBlockKeys,
            RankKeywords: topBlock?.RankKeywords,
            RankTitle: null,
            RankSubtitle: null,
            RankBody: null,
            RankValue: topBlock?.RankValue);
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
