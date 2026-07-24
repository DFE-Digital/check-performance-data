using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Behaviour tests for SearchResultCanonicaliser. Locks the aggregation rule
// (MAX of contributors per URL with contributor-count DESC as the exact-tie
// tie-break), the snippet-selection rule (snippet from the single
// highest-ranked contributor per URL, page beats block on raw-rank ties),
// the ordering rule (aggregate rank DESC), and the purity invariant (same
// inputs -> same outputs; inputs are not mutated).
//
// Trait scheme:
//   [Trait("search-case", "duplicate-blocks")]        — the existing coverage
//     slug for two-blocks-on-one-page dedup, folded into the new aggregation
//     surface.
//   [Trait("search-case", "canonical-aggregation")]   — new documentation-
//     only slug for the aggregation-shape facts. NOT added to
//     SearchCaseCoverageTests.ExpectedCases (that set stays a strict
//     enforcement list; this slug is here purely so a future contributor
//     grepping the trait names sees where aggregation is covered).
public sealed class SearchResultCanonicaliserTests
{
    private static readonly Guid PageAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PageBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static PageContribution PageAt(string url, string title, string snippet, float rank, Guid? pageId = null) =>
        new(url, title, snippet, rank, pageId ?? PageAId);

    private static BlockContribution BlockAt(string url, string pageTitle, string snippet, float rank, string key) =>
        new(url, pageTitle, snippet, rank, key);

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_EmptyInputs_ReturnsEmpty()
    {
        var sut = new SearchResultCanonicaliser();

        var result = sut.Canonicalise([], []);

        Assert.Empty(result);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_SinglePageContributor_UsesPageRankTitleAndSnippet()
    {
        var sut = new SearchResultCanonicaliser();
        var page = PageAt("/x", "X Page", "page snippet", 0.5f);

        var result = sut.Canonicalise([page], []);

        var hit = Assert.Single(result);
        Assert.Equal("/x", hit.Url);
        Assert.Equal("X Page", hit.Title);
        Assert.Equal("page snippet", hit.SnippetHtml);
        Assert.Equal(0.5f, hit.AggregateRank);
        Assert.Equal(1, hit.PageContributorCount);
        Assert.Equal(0, hit.BlockContributorCount);
        Assert.Empty(hit.ContributingBlockKeys);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_SingleBlockContributor_UsesBlockPageTitleAndSnippet()
    {
        var sut = new SearchResultCanonicaliser();
        var block = BlockAt("/x", "X Page", "block snippet", 0.7f, "hero");

        var result = sut.Canonicalise([], [block]);

        var hit = Assert.Single(result);
        Assert.Equal("/x", hit.Url);
        Assert.Equal("X Page", hit.Title);
        Assert.Equal("block snippet", hit.SnippetHtml);
        Assert.Equal(0.7f, hit.AggregateRank);
        Assert.Equal(0, hit.PageContributorCount);
        Assert.Equal(1, hit.BlockContributorCount);
        Assert.Equal(new[] { "hero" }, hit.ContributingBlockKeys);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_PageAndBlockOnSameUrl_TakesMaxRankAndSnippetFromHigherContributor()
    {
        var sut = new SearchResultCanonicaliser();
        var page = PageAt("/x", "X Page", "page snippet", 0.5f);
        var block = BlockAt("/x", "X Page", "block snippet", 0.7f, "hero");

        var result = sut.Canonicalise([page], [block]);

        var hit = Assert.Single(result);
        Assert.Equal(0.7f, hit.AggregateRank);
        Assert.Equal(1, hit.PageContributorCount);
        Assert.Equal(1, hit.BlockContributorCount);
        Assert.Equal("block snippet", hit.SnippetHtml);
        Assert.Equal("X Page", hit.Title);
        Assert.Equal(new[] { "hero" }, hit.ContributingBlockKeys);
    }

    [Fact]
    [Trait("search-case", "duplicate-blocks")]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_TwoBlocksOnSameUrl_FoldsToSingleRowWithMaxRankAndHighestBlockSnippet()
    {
        var sut = new SearchResultCanonicaliser();
        var hero = BlockAt("/guidance", "Guidance", "hero snippet", 0.6f, "hero");
        var footer = BlockAt("/guidance", "Guidance", "footer snippet", 0.8f, "footer");

        var result = sut.Canonicalise([], [hero, footer]);

        var hit = Assert.Single(result);
        Assert.Equal("/guidance", hit.Url);
        Assert.Equal(0.8f, hit.AggregateRank);
        Assert.Equal(0, hit.PageContributorCount);
        Assert.Equal(2, hit.BlockContributorCount);
        Assert.Equal("footer snippet", hit.SnippetHtml);
        Assert.Contains("hero", hit.ContributingBlockKeys);
        Assert.Contains("footer", hit.ContributingBlockKeys);
        Assert.Equal(2, hit.ContributingBlockKeys.Count);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_MultipleUrls_OrdersByAggregateRankDescending()
    {
        var sut = new SearchResultCanonicaliser();
        var low = PageAt("/low", "Low", "low snippet", 0.2f, PageAId);
        var mid = PageAt("/mid", "Mid", "mid snippet", 0.5f, PageBId);
        var high = PageAt("/high", "High", "high snippet", 0.9f, PageAId);

        var result = sut.Canonicalise([low, mid, high], []);

        Assert.Equal(3, result.Count);
        Assert.Equal("/high", result[0].Url);
        Assert.Equal("/mid", result[1].Url);
        Assert.Equal("/low", result[2].Url);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_ExactRankTie_SortsUrlWithMoreContributorsFirst()
    {
        var sut = new SearchResultCanonicaliser();
        // /a has 3 contributors, all rank 0.5 (page + two blocks)
        var pageA = PageAt("/a", "A", "a page snippet", 0.5f, PageAId);
        var blockA1 = BlockAt("/a", "A", "a block-1 snippet", 0.5f, "a1");
        var blockA2 = BlockAt("/a", "A", "a block-2 snippet", 0.5f, "a2");
        // /b has 1 contributor at rank 0.5 (page only)
        var pageB = PageAt("/b", "B", "b page snippet", 0.5f, PageBId);

        var result = sut.Canonicalise([pageA, pageB], [blockA1, blockA2]);

        Assert.Equal(2, result.Count);
        Assert.Equal("/a", result[0].Url);
        Assert.Equal(3, result[0].PageContributorCount + result[0].BlockContributorCount);
        Assert.Equal("/b", result[1].Url);
        Assert.Equal(1, result[1].PageContributorCount + result[1].BlockContributorCount);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_PageOutranksBlock_SnippetFromPage()
    {
        var sut = new SearchResultCanonicaliser();
        var page = PageAt("/x", "X Page", "page snippet", 0.9f);
        var block = BlockAt("/x", "X Page", "block snippet", 0.4f, "hero");

        var result = sut.Canonicalise([page], [block]);

        var hit = Assert.Single(result);
        Assert.Equal(0.9f, hit.AggregateRank);
        Assert.Equal("page snippet", hit.SnippetHtml);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_CalledTwiceWithSameInputs_ReturnsEqualLists()
    {
        var sut = new SearchResultCanonicaliser();
        var page = PageAt("/x", "X Page", "page snippet", 0.5f);
        var block = BlockAt("/x", "X Page", "block snippet", 0.7f, "hero");
        var pages = new[] { page };
        var blocks = new[] { block };

        var first = sut.Canonicalise(pages, blocks);
        var second = sut.Canonicalise(pages, blocks);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            // Compare property-wise: records equal by value, but their
            // IReadOnlyList<string> field would compare by reference and the
            // canonicaliser materialises a fresh array per call by design.
            Assert.Equal(first[i].Url, second[i].Url);
            Assert.Equal(first[i].Title, second[i].Title);
            Assert.Equal(first[i].SnippetHtml, second[i].SnippetHtml);
            Assert.Equal(first[i].AggregateRank, second[i].AggregateRank);
            Assert.Equal(first[i].PageContributorCount, second[i].PageContributorCount);
            Assert.Equal(first[i].BlockContributorCount, second[i].BlockContributorCount);
            Assert.True(first[i].ContributingBlockKeys.SequenceEqual(second[i].ContributingBlockKeys));
        }
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_DoesNotMutateInputs()
    {
        var sut = new SearchResultCanonicaliser();
        var pages = new List<PageContribution> { PageAt("/x", "X", "s", 0.5f) };
        var blocks = new List<BlockContribution> { BlockAt("/x", "X", "s", 0.7f, "hero") };
        var pagesBefore = pages.ToArray();
        var blocksBefore = blocks.ToArray();

        _ = sut.Canonicalise(pages, blocks);

        Assert.Equal(pagesBefore.Length, pages.Count);
        Assert.Equal(blocksBefore.Length, blocks.Count);
        for (var i = 0; i < pagesBefore.Length; i++)
        {
            Assert.Equal(pagesBefore[i], pages[i]);
        }
        for (var i = 0; i < blocksBefore.Length; i++)
        {
            Assert.Equal(blocksBefore[i], blocks[i]);
        }
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_DuplicateBlockKeys_TreatedAsSeparateContributors()
    {
        var sut = new SearchResultCanonicaliser();
        var blockA = BlockAt("/x", "X", "first snippet", 0.4f, "hero");
        var blockB = BlockAt("/x", "X", "second snippet", 0.6f, "hero");

        var result = sut.Canonicalise([], [blockA, blockB]);

        var hit = Assert.Single(result);
        Assert.Equal(2, hit.BlockContributorCount);
        Assert.Equal(2, hit.ContributingBlockKeys.Count);
        Assert.All(hit.ContributingBlockKeys, key => Assert.Equal("hero", key));
        Assert.Equal(0.6f, hit.AggregateRank);
        Assert.Equal("second snippet", hit.SnippetHtml);
    }

    [Fact]
    [Trait("search-case", "canonical-aggregation")]
    public void Canonicalise_UrlComparisonIsOrdinal_DifferentCasesAreDistinctRows()
    {
        var sut = new SearchResultCanonicaliser();
        var lower = PageAt("/x", "X lower", "lower snippet", 0.5f, PageAId);
        var upper = PageAt("/X", "X upper", "upper snippet", 0.5f, PageBId);

        var result = sut.Canonicalise([lower, upper], []);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, h => h.Url == "/x");
        Assert.Contains(result, h => h.Url == "/X");
    }
}
