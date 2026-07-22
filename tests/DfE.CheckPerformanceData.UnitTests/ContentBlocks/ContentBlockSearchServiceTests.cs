using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

public sealed class ContentBlockSearchServiceTests
{
    private readonly IContentBlockRepository _repository = Substitute.For<IContentBlockRepository>();
    private readonly IPageNodeRepository _pageNodeRepository = Substitute.For<IPageNodeRepository>();
    private readonly IHtmlRenderingService _html = Substitute.For<IHtmlRenderingService>();
    private readonly ContentBlockSearchService _sut;

    public ContentBlockSearchServiceTests()
    {
        _sut = new ContentBlockSearchService(_repository, _pageNodeRepository, _html);

        // Render + strip pass the value straight through so tests control the plain text
        // the snippet builder sees.
        _html.RenderHtml(Arg.Any<string>()).Returns(ci => (string)ci[0]);
        _html.StripTagsToPlainText(Arg.Any<string>()).Returns(ci => (string)ci[0]);

        // Default: no page node exists for any lookup — individual tests override.
        _pageNodeRepository.GetPublishedByPathAsync(Arg.Any<string>())
            .Returns((PublishedPageInfoDto?)null);
    }

    // --- Query guard ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [Trait("prd-case", "G")]
    [Trait("prd-case", "I")]
    public async Task SearchAsync_ShortOrEmptyQuery_ReturnsEmpty_AndDoesNotHitRepository(string? query)
    {
        var result = await _sut.SearchAsync(query);

        Assert.Empty(result);
        await _repository.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    // --- Over-fetch + de-duplication by resolved URL ---

    [Fact]
    public async Task SearchAsync_OverFetchesByThree_ThenDeduplicatesByLastSeenPath()
    {
        // Two blocks that were both last rendered on the same public page.
        var path = "/guidance/ks4-june-2026-key-dates";
        _pageNodeRepository.GetPublishedByPathAsync(path)
            .Returns(new PublishedPageInfoDto(path, "Key dates — KS4 June 2026"));

        _repository.SearchAsync("key", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-key-dates", "Key dates body", path),
            Block("guidance-ks4-2026-key-dates-heading", "Key dates", path)
        ]);

        var result = await _sut.SearchAsync("key", max: 3);

        Assert.Single(result);
        // Over-fetch: repo asked for max * 3 so dedupe still yields up to `max`.
        await _repository.Received(1).SearchAsync("key", 9);
    }

    // --- Filtering: null / admin / unresolved LastSeenPath ---

    [Fact]
    public async Task SearchAsync_SkipsBlocksWithNoLastSeenPath()
    {
        // A block that has never been rendered anywhere has no LastSeenPath.
        // The old hardcoded resolver would still emit a URL for these; the new resolver
        // hides them so users don't land on 404s.
        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("orphan-block", "matches term", lastSeenPath: null)
        ]);

        Assert.Empty(await _sut.SearchAsync("term"));
    }

    [Fact]
    [Trait("prd-filter", "admin-path")]
    [Trait("prd-case", "L")]
    public async Task SearchAsync_SkipsBlocksLastRenderedOnAdminPage()
    {
        // Admin/editor renders should never leak into public search — the block was seen
        // there but that's not a linkable destination for a public user.
        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("some-block", "matches term", lastSeenPath: "/admin/content-blocks/edit/some-block")
        ]);

        Assert.Empty(await _sut.SearchAsync("term"));
    }

    [Fact]
    [Trait("prd-filter", "unpublished-target")]
    [Trait("prd-case", "N")]
    public async Task SearchAsync_SkipsBlocksWhoseLastSeenPathIsNoLongerAPublishedPage()
    {
        // Page has since been unpublished / soft-deleted / never had a live version.
        // GetPublishedByPathAsync returns null for that path -> the block is hidden.
        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("stale-block", "matches term", lastSeenPath: "/guidance/removed-page")
        ]);

        // Default stub returns null; no override needed.

        Assert.Empty(await _sut.SearchAsync("term"));
    }

    // --- Success path: published PageNode + static routes ---

    [Fact]
    public async Task SearchAsync_UsesLastSeenPath_AsUrl_AndPageNodeTitle_AsTitle()
    {
        var path = "/guidance/ks4-june-2026-get-support";
        _pageNodeRepository.GetPublishedByPathAsync(path)
            .Returns(new PublishedPageInfoDto(path, "Get support — KS4 June 2026"));

        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-get-support", "Contact the helpdesk", lastSeenPath: path)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("term"));

        Assert.Equal("guidance-ks4-2026-get-support", hit.Key);
        Assert.Equal(path, hit.Url);
        Assert.Equal("Get support — KS4 June 2026", hit.PageTitle);
    }

    [Fact]
    [Trait("prd-filter", "pagenode-appearinsearch-false")]
    [Trait("prd-case", "L")]
    public async Task SearchAsync_WhenPageAtLastSeenPath_HasAppearInSearch_False_TheBlockIsHidden()
    {
        // GetPublishedByPathAsync already applies the AppearInSearch filter server-side, so a page
        // toggled out of search returns null from that lookup — mirroring the DB behaviour keeps
        // this test faithful to the real code path.
        var path = "/guidance/toggled-off-page";
        _pageNodeRepository.GetPublishedByPathAsync(path).Returns((PublishedPageInfoDto?)null);

        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("some-block", "matches term", lastSeenPath: path)
        ]);

        Assert.Empty(await _sut.SearchAsync("term"));
    }

    [Fact]
    public async Task SearchAsync_ResolvesRootPath_AsHome()
    {
        // "/" is not backed by a PageNode — it's rendered by a Razor view. The whitelist
        // of static routes covers this case with an explicit title.
        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("home-content", "matches term", lastSeenPath: "/")
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("term"));

        Assert.Equal("/", hit.Url);
        Assert.Equal("Home", hit.PageTitle);
    }

    // --- Cap ---

    [Fact]
    public async Task SearchAsync_CapsResultsAtMax()
    {
        StubPublished("/guidance/one", "One");
        StubPublished("/guidance/two", "Two");
        StubPublished("/guidance/three", "Three");

        _repository.SearchAsync("ab", Arg.Any<int>()).Returns(
        [
            Block("k1", "ab", "/guidance/one"),
            Block("k2", "ab", "/guidance/two"),
            Block("k3", "ab", "/guidance/three")
        ]);

        var result = await _sut.SearchAsync("ab", max: 2);

        Assert.Equal(2, result.Count);
    }

    // --- Snippet building: the security-relevant <mark> + HTML-encode contract ---

    [Fact]
    [Trait("prd-case", "J")]
    public async Task SearchAsync_Snippet_HtmlEncodesSurroundingText_AndWrapsMatchInMark()
    {
        var path = "/guidance/ks4-june-2026-publish";
        StubPublished(path, "Publish info");

        _repository.SearchAsync("publish", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-published", "<script>evil()</script> publish date <b>x</b>", path)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("publish"));

        Assert.Contains("<mark>publish</mark>", hit.SnippetHtml);
        // Surrounding markup is encoded — the only live tag is the <mark>.
        Assert.Contains("&lt;script&gt;", hit.SnippetHtml);
        Assert.DoesNotContain("<script>", hit.SnippetHtml);
        Assert.DoesNotContain("<b>", hit.SnippetHtml);
    }

    [Fact]
    public async Task SearchAsync_Snippet_WhenTermNotInBody_TruncatesAt200WithEllipsis()
    {
        var path = "/guidance/ks4-june-2026-about-this-guidance";
        StubPublished(path, "About this guidance");

        var body = new string('x', 300);
        _repository.SearchAsync("zz", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-about-this-guidance", body, path)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("zz"));

        Assert.EndsWith("…", hit.SnippetHtml);
        Assert.DoesNotContain("<mark>", hit.SnippetHtml);
        Assert.Equal(201, hit.SnippetHtml.Length);
    }

    [Fact]
    public async Task SearchAsync_Snippet_WhenMatchIsDeepInBody_AddsLeadingEllipsis()
    {
        var path = "/guidance/ks4-june-2026-how-to-check-your-data";
        StubPublished(path, "How to check your data");

        var body = new string('a', 120) + "needle" + new string('b', 120);
        _repository.SearchAsync("needle", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-how-to-check-your-data", body, path)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("needle"));

        Assert.StartsWith("…", hit.SnippetHtml);
        Assert.Contains("<mark>needle</mark>", hit.SnippetHtml);
    }

    private void StubPublished(string path, string title) =>
        _pageNodeRepository.GetPublishedByPathAsync(path)
            .Returns(new PublishedPageInfoDto(path, title));

    private static ContentBlockDto Block(string key, string value, string? lastSeenPath = null) => new()
    {
        Id = 1,
        Key = key,
        BlockType = "Content",
        Value = value,
        LastSeenPath = lastSeenPath
    };
}
