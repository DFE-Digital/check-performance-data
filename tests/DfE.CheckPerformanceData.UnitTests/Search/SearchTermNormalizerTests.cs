using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Locks the shipped behaviour of SearchTermNormalizer.OrJoinWhitespace: the whitespace →
// " OR " join used to soften Google-style AND-by-default in Postgres' websearch_to_tsquery,
// and the pass-through path for queries that already carry websearch syntax (an explicit
// OR token, a quoted "phrase", or a leading -negation). Also pins Unicode case-preservation
// and the observed whitespace/empty edge cases.
//
// The method-level [Trait("search-case", <slug>)] attributes are load-bearing — a downstream
// coverage meta-test sweeps this file for the search-behaviour cases documented there.
// Attaching at the method level (not the class level) is the only shape the meta-test
// recognises.
public sealed class SearchTermNormalizerTests
{
    // Single-word input is returned unchanged.
    [Theory]
    [Trait("search-case", "single-word")]
    [InlineData("merge", "merge")]
    public void OrJoinWhitespace_SingleWord_ReturnsInput(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Multi-word bare input with no websearch operators is joined with " OR " so that either
    // term hits (rows matching all terms still outrank via ts_rank).
    [Theory]
    [Trait("search-case", "multi-word-or")]
    [InlineData("merge booga", "merge OR booga")]
    [InlineData("one two three", "one OR two OR three")]
    public void OrJoinWhitespace_MultiWordBare_JoinsWithOr(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // An explicit "OR" bare token anywhere in the query signals websearch intent, and the
    // whole string is passed through untouched (even mixed operator+bare shapes such as
    // "merge OR booga fizz" — the operator token is enough to disable the join).
    [Theory]
    [Trait("search-case", "explicit-or")]
    [InlineData("merge OR booga", "merge OR booga")]
    [InlineData("merge OR booga fizz", "merge OR booga fizz")]
    [InlineData("one OR two three", "one OR two three")]
    public void OrJoinWhitespace_ExplicitOrOrMixed_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Any double-quote character in the input triggers pass-through so the quoted phrase
    // reaches Postgres intact.
    [Theory]
    [Trait("search-case", "phrase")]
    [InlineData("\"pupil records\"", "\"pupil records\"")]
    public void OrJoinWhitespace_QuotedPhrase_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // A bare token whose first char is '-' signals websearch negation intent, so the whole
    // query is passed through untouched.
    [Theory]
    [Trait("search-case", "negation")]
    [InlineData("pupil -deleted", "pupil -deleted")]
    public void OrJoinWhitespace_LeadingHyphenNegation_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Whitespace-only input is NOT trimmed to empty; string.IsNullOrWhiteSpace short-circuits
    // before the Trim() call, so the original whitespace string is echoed back. Empty input
    // echoes empty. This pins the observed behaviour of the current normaliser.
    [Theory]
    [Trait("search-case", "empty-or-whitespace")]
    [InlineData("   ", "   ")]
    [InlineData("", "")]
    public void OrJoinWhitespace_WhitespaceOrEmpty_ObservedBehaviour(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Tab / newline / carriage-return count as split separators alongside space, so a
    // tab-separated or CR/LF-separated query behaves identically to a space-separated one
    // under the bare multi-word join.
    [Theory]
    [Trait("search-case", "empty-or-whitespace")]
    [InlineData("a\tb", "a OR b")]
    [InlineData("a\nb\rc", "a OR b OR c")]
    public void OrJoinWhitespace_TabAndNewlineSeparators_SplitAndJoin(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Unicode words (accented, non-ASCII) pass through with case preserved.
    // OrJoinWhitespace is case-preserving; it does NOT lowercase the input.
    [Theory]
    [Trait("search-case", "special-chars")]
    [InlineData("café", "café")]
    public void OrJoinWhitespace_UnicodeWord_PreservesCase(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }
}
