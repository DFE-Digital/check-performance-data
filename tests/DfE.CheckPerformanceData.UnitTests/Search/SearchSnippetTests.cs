using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Pins the snippet-safety contract for the shared SearchSnippet.BuildWindow helper —
// the windowed HTML-encode + <mark> wrap used by SiteSearchService and
// ContentBlockSearchService to build every search-result snippet. The only markup that
// may reach the results page is the <mark>...</mark> around the matched term. Every
// other character in the surrounding window is HTML-encoded via WebUtility.HtmlEncode.
//
// The two-sided-sink assertion pattern (both encoded form present AND raw form absent)
// mirrors the existing guard on ContentBlockSearchServiceTests:189-206 — a single-sided
// "encoded form present" would pass on double-encoded output where the raw tag also
// leaked through. Both halves are load-bearing.
public sealed class SearchSnippetTests
{
    // Six rows without a search-case trait: apostrophe, angle-bracket <b>, straight quote,
    // nested quote+apostrophe, already-HTML-encoded input, match at very end.
    [Theory]
    [InlineData(
        "pupil O'Brien loves widgets", "widget",
        "<mark>widget</mark>", "O&#39;Brien", "")]
    [InlineData(
        "I saw <b>widgets</b> here", "widgets",
        "<mark>widgets</mark>", "&lt;b&gt;", "<b>")]
    [InlineData(
        "He said \"widget\" today", "widget",
        "<mark>widget</mark>", "&quot;", "")]
    [InlineData(
        "pupil \"O'Brien\"", "O'Brien",
        "<mark>O&#39;Brien</mark>", "&quot;", "")]
    [InlineData(
        "&lt;widget&gt; already encoded", "widget",
        "<mark>widget</mark>", "&amp;lt;", "<widget>")]
    [InlineData(
        "prefix prefix widget", "widget",
        "<mark>widget</mark>", "", "")]
    public void BuildWindow_XssShapedInput_EncodesSurroundingText_AndWrapsOnlyMatchInMark(
        string body, string term, string expectedMarkWrap, string mustContainEncoded, string mustNotContainRaw)
    {
        var result = SearchSnippet.BuildWindow(body, term);

        // The matched term is the only unencoded markup — always wrapped in <mark>.
        Assert.Contains(expectedMarkWrap, result);

        if (mustContainEncoded.Length > 0)
            Assert.Contains(mustContainEncoded, result);

        if (mustNotContainRaw.Length > 0)
            Assert.DoesNotContain(mustNotContainRaw, result);
    }

    // Special-character / XSS-shaped input: the <script> row proves the raw tag is scrubbed
    // AND the encoded form is emitted; the Unicode row proves CJK survives the encode +
    // mark wrap unmodified. Trait attaches at method level so the coverage meta-test's
    // reflection walk sees it (class-level traits don't inherit unless the walker opts in).
    [Theory]
    [Trait("search-case", "special-chars")]
    [InlineData(
        "Hello <script>alert(1)</script> widget", "widget",
        "<mark>widget</mark>", "&lt;script&gt;", "<script>")]
    [InlineData(
        "日本 widget", "widget",
        "<mark>widget</mark>", "日本", "")]
    public void BuildWindow_ScriptOrUnicodeInput_EncodesSurroundingText_AndWrapsOnlyMatchInMark_SpecialChars(
        string body, string term, string expectedMarkWrap, string mustContainEncoded, string mustNotContainRaw)
    {
        var result = SearchSnippet.BuildWindow(body, term);

        Assert.Contains(expectedMarkWrap, result);

        if (mustContainEncoded.Length > 0)
            Assert.Contains(mustContainEncoded, result);

        if (mustNotContainRaw.Length > 0)
            Assert.DoesNotContain(mustNotContainRaw, result);
    }

    // The two callers protect against null in slightly different ways today
    // (SiteSearchService relies on non-null body from EF, ContentBlockSearchService
    // uses `plainText ?? string.Empty`). The shared helper handles null centrally so
    // neither caller has to.
    [Fact]
    public void BuildWindow_NullSource_ReturnsEmptyString_DoesNotThrow()
    {
        var result = SearchSnippet.BuildWindow(null, "widget");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildWindow_EmptySource_ReturnsEmptyString_DoesNotThrow()
    {
        var result = SearchSnippet.BuildWindow(string.Empty, "widget");

        Assert.Equal(string.Empty, result);
    }
}
