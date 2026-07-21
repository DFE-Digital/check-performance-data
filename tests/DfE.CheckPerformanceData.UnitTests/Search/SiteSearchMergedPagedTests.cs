using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract for the merged/paged view the results widget renders. Two axes matter:
//   1. Page hits and content-block hits interleave by rank into a single output — the
//      viewer should not see "Pages" / "Content blocks" section headers.
//   2. Skip/take semantics are stable so the pager navigates predictably.
public sealed class SiteSearchMergedPagedTests
{
    private readonly IPageNodeRepository _pageRepo = Substitute.For<IPageNodeRepository>();
    private readonly IContentBlockSearchService _blockSearch = Substitute.For<IContentBlockSearchService>();
    private readonly SiteSearchService _sut;

    public SiteSearchMergedPagedTests()
    {
        _sut = new SiteSearchService(_pageRepo, _blockSearch);
    }

    [Fact]
    public async Task InvalidQuery_ReturnsEmptyPagedResult_WithReason()
    {
        var result = await _sut.SearchMergedPagedAsync(
            new SiteSearchQuery(Query: ""),
            page: 0,
            pageSize: 20);

        Assert.Equal(SearchInvalidReason.EmptyQuery, result.InvalidReason);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Merges_PageAndBlockHits_IntoOneRankedList()
    {
        _pageRepo.SearchPagesAsync("pupil", null, Arg.Any<int>())
            .Returns([
                Page(rank: 0.5f,  path: "guidance/mid",  title: "Guidance mid"),
                Page(rank: 0.9f,  path: "guidance/top",  title: "Guidance top"),
            ]);
        _blockSearch.SearchAsync("pupil", Arg.Any<int>())
            .Returns([
                Block(rank: 0.7f, url: "/help/faq#a",     pageTitle: "Help — FAQ"),
                Block(rank: 0.3f, url: "/help/faq#b",     pageTitle: "Help — FAQ tail"),
            ]);

        var result = await _sut.SearchMergedPagedAsync(
            new SiteSearchQuery("pupil"),
            page: 0,
            pageSize: 20);

        Assert.Equal(4, result.TotalCount);
        Assert.Collection(result.Items,
            i => Assert.Equal("Guidance top", i.Title),     // rank 0.9
            i => Assert.Equal("Help — FAQ", i.Title),        // rank 0.7
            i => Assert.Equal("Guidance mid", i.Title),      // rank 0.5
            i => Assert.Equal("Help — FAQ tail", i.Title));  // rank 0.3
    }

    [Fact]
    public async Task PageUrls_MissingLeadingSlash_GetOnePrepended()
    {
        _pageRepo.SearchPagesAsync("xy", null, Arg.Any<int>())
            .Returns([Page(rank: 1.0f, path: "guidance/overview", title: "Overview")]);
        _blockSearch.SearchAsync("xy", Arg.Any<int>()).Returns([]);

        var result = await _sut.SearchMergedPagedAsync(
            new SiteSearchQuery("xy"), page: 0, pageSize: 20);

        Assert.Equal("/guidance/overview", result.Items.Single().Url);
    }

    [Fact]
    public async Task Paging_TakesTheRequestedSlice()
    {
        _pageRepo.SearchPagesAsync("wide", null, Arg.Any<int>())
            .Returns(Enumerable.Range(1, 30)
                .Select(i => Page(rank: 100f - i, path: $"p/{i}", title: $"Page {i:D2}"))
                .ToList());
        _blockSearch.SearchAsync("wide", Arg.Any<int>()).Returns([]);

        // Second page (zero-indexed), size 10 — expects hits 11..20.
        var result = await _sut.SearchMergedPagedAsync(
            new SiteSearchQuery("wide"), page: 1, pageSize: 10);

        Assert.Equal(30, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("Page 11", result.Items.First().Title);
        Assert.Equal("Page 20", result.Items.Last().Title);
    }

    [Fact]
    public async Task NegativePage_ClampsToZero()
    {
        _pageRepo.SearchPagesAsync("xy", null, Arg.Any<int>())
            .Returns([Page(rank: 1.0f, path: "p/1", title: "P1")]);
        _blockSearch.SearchAsync("xy", Arg.Any<int>()).Returns([]);

        var result = await _sut.SearchMergedPagedAsync(
            new SiteSearchQuery("xy"), page: -5, pageSize: 10);

        Assert.Equal(0, result.Page);
        Assert.Single(result.Items);
    }

    private static PageSearchHitRaw Page(float rank, string path, string title) =>
        new()
        {
            PageId = Guid.NewGuid(),
            Path = path,
            Title = title,
            Subtitle = null,
            BodyPlainText = string.Empty,
            Rank = rank,
        };

    private static ContentBlockSearchResultDto Block(float rank, string url, string pageTitle) =>
        new()
        {
            Key = Guid.NewGuid().ToString(),
            Url = url,
            PageTitle = pageTitle,
            SnippetHtml = string.Empty,
            Rank = rank,
        };
}
