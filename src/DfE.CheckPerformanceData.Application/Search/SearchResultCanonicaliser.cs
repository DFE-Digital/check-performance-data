namespace DfE.CheckPerformanceData.Application.Search;

/// <summary>
/// Pure implementation of <see cref="ISearchResultCanonicaliser"/>.
/// </summary>
/// <remarks>
/// The parameterless constructor is load-bearing — it makes it impossible to
/// inject a logger, options object, repository, or clock without a signature
/// change caught in code review. All logic depends only on the two input
/// enumerables.
/// </remarks>
public sealed class SearchResultCanonicaliser : ISearchResultCanonicaliser
{
    public IReadOnlyList<CanonicalSearchHit> Canonicalise(
        IEnumerable<PageContribution> pages,
        IEnumerable<BlockContribution> blocks)
    {
        // Materialise once so both the grouping pass and any later re-enumeration
        // see the same sequence. The interface accepts IEnumerable<T> so callers
        // can pass lazy sequences; we take a single snapshot rather than iterating
        // twice.
        var pageList = pages as IReadOnlyList<PageContribution> ?? pages.ToArray();
        var blockList = blocks as IReadOnlyList<BlockContribution> ?? blocks.ToArray();

        // Collect the set of URLs from both corpora, preserving first-seen order
        // so exact-tie ordering is deterministic across runs. Pages come first
        // (matches the wire order in SiteSearchService: pages fetched, then
        // blocks appended); block-only URLs slot in after in the order they
        // arrive.
        var urlOrder = new List<string>();
        var urlIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var p in pageList)
        {
            if (!urlIndex.ContainsKey(p.Url))
            {
                urlIndex[p.Url] = urlOrder.Count;
                urlOrder.Add(p.Url);
            }
        }
        foreach (var b in blockList)
        {
            if (!urlIndex.ContainsKey(b.Url))
            {
                urlIndex[b.Url] = urlOrder.Count;
                urlOrder.Add(b.Url);
            }
        }

        var hits = new List<CanonicalSearchHit>(urlOrder.Count);

        foreach (var url in urlOrder)
        {
            // A URL has at most one page contributor by construction (the page
            // corpus is keyed by path). If multiple were passed in we still take
            // the first — repository invariants make this a non-case.
            PageContribution? pageContributor = null;
            var pageCount = 0;
            foreach (var p in pageList)
            {
                if (StringComparer.Ordinal.Equals(p.Url, url))
                {
                    if (pageContributor is null)
                    {
                        pageContributor = p;
                    }
                    pageCount++;
                }
            }

            var blockContributors = new List<BlockContribution>();
            foreach (var b in blockList)
            {
                if (StringComparer.Ordinal.Equals(b.Url, url))
                {
                    blockContributors.Add(b);
                }
            }

            var aggregateRank = pageContributor?.Rank ?? float.NegativeInfinity;
            foreach (var b in blockContributors)
            {
                if (b.Rank > aggregateRank)
                {
                    aggregateRank = b.Rank;
                }
            }
            // Missing page contributor was represented with NegativeInfinity to
            // keep the max-comparison simple; if no block contributor lifted it,
            // fall back to zero (only reachable if both counts are zero, which
            // cannot happen — a url with no contributors would never be in
            // urlOrder).
            if (float.IsNegativeInfinity(aggregateRank))
            {
                aggregateRank = 0f;
            }

            // Snippet source = single highest-ranked contributor. Ties broken by
            // "page beats block" then "input order" (already the case because we
            // scan the page first and only replace on strictly-greater rank).
            var snippet = pageContributor?.SnippetHtml ?? string.Empty;
            var title = pageContributor?.Title
                        ?? (blockContributors.Count > 0 ? blockContributors[0].PageTitle : string.Empty);
            var winningRank = pageContributor?.Rank ?? float.NegativeInfinity;

            foreach (var b in blockContributors)
            {
                if (b.Rank > winningRank)
                {
                    snippet = b.SnippetHtml;
                    winningRank = b.Rank;
                }
            }

            var contributingKeys = new string[blockContributors.Count];
            for (var i = 0; i < blockContributors.Count; i++)
            {
                contributingKeys[i] = blockContributors[i].BlockKey;
            }

            hits.Add(new CanonicalSearchHit(
                Url: url,
                Title: title,
                SnippetHtml: snippet,
                AggregateRank: aggregateRank,
                PageContributorCount: pageContributor is null ? 0 : 1,
                BlockContributorCount: blockContributors.Count,
                ContributingBlockKeys: contributingKeys));
        }

        // Primary sort: aggregate rank desc. Secondary sort: total contributor
        // count desc — only affects exact-tie rows. OrderByDescending is stable
        // in .NET, so within-tie ordering falls back to the first-seen url order
        // captured above.
        return hits
            .OrderByDescending(h => h.AggregateRank)
            .ThenByDescending(h => h.PageContributorCount + h.BlockContributorCount)
            .ToArray();
    }
}
