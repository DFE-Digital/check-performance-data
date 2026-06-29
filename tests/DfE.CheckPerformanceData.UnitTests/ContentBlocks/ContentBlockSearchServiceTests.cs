using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

public sealed class ContentBlockSearchServiceTests
{
    private readonly IContentBlockRepository _repository = Substitute.For<IContentBlockRepository>();
    private readonly IHtmlRenderingService _html = Substitute.For<IHtmlRenderingService>();
    private readonly ContentBlockSearchService _sut;

    public ContentBlockSearchServiceTests()
    {
        _sut = new ContentBlockSearchService(_repository, _html);

        // Render + strip pass the value straight through so tests control the plain text
        // the snippet builder sees.
        _html.RenderHtml(Arg.Any<string>()).Returns(ci => (string)ci[0]);
        _html.StripTagsToPlainText(Arg.Any<string>()).Returns(ci => (string)ci[0]);
    }

    // --- Query guard ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task SearchAsync_ShortOrEmptyQuery_ReturnsEmpty_AndDoesNotHitRepository(string? query)
    {
        var result = await _sut.SearchAsync(query);

        Assert.Empty(result);
        await _repository.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    // --- Over-fetch + de-duplication by resolved URL ---

    [Fact]
    public async Task SearchAsync_OverFetchesByThree_ThenDeduplicatesByResolvedUrl()
    {
        // A section body block and its heading block both resolve to the same #anchor URL.
        _repository.SearchAsync("key", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-key-dates", "Key dates body"),
            Block("guidance-ks4-2026-key-dates-heading", "Key dates")
        ]);

        var result = await _sut.SearchAsync("key", max: 3);

        Assert.Single(result);
        // Over-fetch: repo asked for max * 3 so dedupe still yields up to `max`.
        await _repository.Received(1).SearchAsync("key", 9);
    }

    [Fact]
    public async Task SearchAsync_SkipsBlocksThatDoNotResolveToAPage()
    {
        _repository.SearchAsync("term", Arg.Any<int>()).Returns(
        [
            Block("e2e-seed-block", "noise"),
            Block("some-unknown-key", "noise"),
            Block("guidance-ks4-2026-get-support", "Contact the helpdesk")
        ]);

        var result = await _sut.SearchAsync("term");

        var hit = Assert.Single(result);
        Assert.Equal("guidance-ks4-2026-get-support", hit.Key);
        Assert.Equal("/guidance/2026-ks4-june-checking-exercise#get-support", hit.Url);
    }

    [Fact]
    public async Task SearchAsync_CapsResultsAtMax()
    {
        _repository.SearchAsync("ab", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-key-dates", "ab"),
            Block("guidance-ks4-2026-get-support", "ab"),
            Block("guidance-ks4-2026-about-this-guidance", "ab")
        ]);

        var result = await _sut.SearchAsync("ab", max: 2);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchAsync_MapsKeyUrlAndPageTitleFromBlockAndLocation()
    {
        _repository.SearchAsync("date", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-key-dates", "Important date information")
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("date"));

        Assert.Equal("guidance-ks4-2026-key-dates", hit.Key);
        Assert.Equal("/guidance/2026-ks4-june-checking-exercise#key-dates", hit.Url);
        Assert.False(string.IsNullOrWhiteSpace(hit.PageTitle));
    }

    // --- Snippet building: the security-relevant <mark> + HTML-encode contract ---

    [Fact]
    public async Task SearchAsync_Snippet_HtmlEncodesSurroundingText_AndWrapsMatchInMark()
    {
        _repository.SearchAsync("publish", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-published", "<script>evil()</script> publish date <b>x</b>")
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
        // Repo matched on (say) the key, but the term isn't in the rendered body.
        var body = new string('x', 300);
        _repository.SearchAsync("zz", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-about-this-guidance", body)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("zz"));

        Assert.EndsWith("…", hit.SnippetHtml);
        Assert.DoesNotContain("<mark>", hit.SnippetHtml);
        Assert.Equal(201, hit.SnippetHtml.Length); // 200 chars + the ellipsis
    }

    [Fact]
    public async Task SearchAsync_Snippet_WhenMatchIsDeepInBody_AddsLeadingEllipsis()
    {
        var body = new string('a', 120) + "needle" + new string('b', 120);
        _repository.SearchAsync("needle", Arg.Any<int>()).Returns(
        [
            Block("guidance-ks4-2026-how-to-check-your-data", body)
        ]);

        var hit = Assert.Single(await _sut.SearchAsync("needle"));

        Assert.StartsWith("…", hit.SnippetHtml);
        Assert.Contains("<mark>needle</mark>", hit.SnippetHtml);
    }

    private static ContentBlockDto Block(string key, string value) => new()
    {
        Id = 1,
        Key = key,
        BlockType = "Content",
        Value = value
    };
}
