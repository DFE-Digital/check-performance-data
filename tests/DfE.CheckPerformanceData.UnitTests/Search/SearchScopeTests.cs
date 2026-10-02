using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// A search scope is one page path or several, comma-separated ("guidance/ks4,guidance/results").
// Each path means "this page and everything under it". Page paths never contain a comma, so the
// separator is unambiguous, and a single path parses exactly as it always has.
public class SearchScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void Parse_NoUsablePath_ReturnsEmpty(string? raw)
    {
        Assert.Empty(SearchScope.Parse(raw));
        Assert.Null(SearchScope.Normalise(raw));
    }

    [Fact]
    public void Parse_SinglePath_TrimsSurroundingSlashesAndWhitespace()
    {
        Assert.Equal(["guidance"], SearchScope.Parse(" /guidance/ "));
    }

    [Fact]
    public void Parse_SeveralPaths_KeepsOrderAndDropsBlanksAndDuplicates()
    {
        var parsed = SearchScope.Parse("/guidance/16-to-19/, guidance/results-enquiries,,guidance/16-to-19");

        Assert.Equal(["guidance/16-to-19", "guidance/results-enquiries"], parsed);
    }

    [Fact]
    public void Normalise_ReturnsTheCanonicalCommaSeparatedForm()
    {
        Assert.Equal("a/b,c", SearchScope.Normalise("/a/b/ , c/"));
    }

    [Fact]
    public void Covers_IsTrueForAScopedPathItself_AndForAnythingBeneathIt()
    {
        var scope = SearchScope.Parse("guidance/ks4,guidance/results-enquiries");

        Assert.True(SearchScope.Covers(scope, "/guidance/ks4"));
        Assert.True(SearchScope.Covers(scope, "/guidance/ks4/dates"));
        Assert.True(SearchScope.Covers(scope, "/GUIDANCE/results-enquiries/x"));
    }

    [Fact]
    public void Covers_IsFalseOutsideEveryScopedPath_IncludingPrefixSiblings()
    {
        var scope = SearchScope.Parse("guidance/ks4,guidance/results-enquiries");

        Assert.False(SearchScope.Covers(scope, "/guidance/16-to-19/dates"));
        Assert.False(SearchScope.Covers(scope, "/guidance/ks4-archive/x"));
    }

    [Fact]
    public void Covers_AnEmptyScope_CoversEverything()
    {
        Assert.True(SearchScope.Covers(SearchScope.Parse(null), "/anything"));
    }

    // The search page names its scope in an HTML comment for anyone reading the source. The scope
    // comes from the query string, so the comment text must never be able to end the comment early
    // or open markup of its own.
    [Fact]
    public void ForHtmlComment_ListsTheScopedPagesAsCsv()
    {
        Assert.Equal("guidance/16-to-19,guidance/results-enquiries",
            SearchScope.ForHtmlComment("/guidance/16-to-19/, guidance/results-enquiries"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void ForHtmlComment_NoScope_ReturnsNull(string? raw)
    {
        Assert.Null(SearchScope.ForHtmlComment(raw));
    }

    [Theory]
    [InlineData("--><script>alert(1)</script>")]
    [InlineData("guidance--->x")]
    [InlineData("<!--a-->")]
    [InlineData("a&amp;b\"c'd")]
    [InlineData("--!>")]
    public void ForHtmlComment_HostileScope_CannotCloseOrBreakTheComment(string raw)
    {
        var text = SearchScope.ForHtmlComment(raw) ?? string.Empty;

        Assert.DoesNotContain("--", text);
        Assert.DoesNotContain(">", text);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("&", text);
        Assert.DoesNotContain("!", text);
        Assert.DoesNotContain("\"", text);
        Assert.False(text.EndsWith('-'), "Comment text must not end in a hyphen that could join the closing -->.");
    }

    [Fact]
    public void ForHtmlComment_HostileScope_KeepsOnlyPathCharacters()
    {
        Assert.Equal("scriptalert1/script", SearchScope.ForHtmlComment("--><script>alert(1)</script>"));
    }
}
