using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Locks the shipped behaviour of SearchTermNormalizer.OrJoinWhitespace: the whitespace →
// " OR " join used to soften Google-style AND-by-default in Postgres' websearch_to_tsquery,
// and the pass-through path for queries that already carry websearch syntax (an explicit
// OR token, a quoted "phrase", or a leading -negation). Also pins Unicode case-preservation
// and the observed whitespace/empty edge cases.
//
// The method-level [Trait("prd-case", <letter>)] attributes are load-bearing — a meta-test
// downstream sweeps this file for coverage of the PRD §6 cases (A single-word,
// B multi-word bare, C explicit-OR / mixed, D quoted phrase, E leading-hyphen negation,
// I whitespace / empty / tab / newline, J Unicode). Attaching at the method level (not the
// class level) is the only shape the meta-test recognises.
//
// Every scenario is a Theory + InlineData so both the [InlineData] row count and the
// method-level [Trait] count remain ≥13 / ≥7 respectively (must_haves.truths threshold).
public sealed class SearchTermNormalizerTests
{
    // Case A — single-word input is returned unchanged.
    [Theory]
    [Trait("prd-case", "A")]
    [InlineData("merge", "merge")]
    public void OrJoinWhitespace_SingleWord_ReturnsInput(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case B — multi-word bare input with no websearch operators is joined with " OR "
    // so that either term hits (rows matching all terms still outrank via ts_rank).
    [Theory]
    [Trait("prd-case", "B")]
    [InlineData("merge booga", "merge OR booga")]
    [InlineData("one two three", "one OR two OR three")]
    public void OrJoinWhitespace_MultiWordBare_JoinsWithOr(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case C — an explicit "OR" bare token anywhere in the query signals websearch intent,
    // and the whole string is passed through untouched (even mixed operator+bare shapes
    // such as "merge OR booga fizz" — the operator token is enough to disable the join).
    [Theory]
    [Trait("prd-case", "C")]
    [InlineData("merge OR booga", "merge OR booga")]
    [InlineData("merge OR booga fizz", "merge OR booga fizz")]
    [InlineData("one OR two three", "one OR two three")]
    public void OrJoinWhitespace_ExplicitOrOrMixed_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case D — any double-quote character in the input triggers pass-through so the
    // quoted phrase reaches Postgres intact.
    [Theory]
    [Trait("prd-case", "D")]
    [InlineData("\"pupil records\"", "\"pupil records\"")]
    public void OrJoinWhitespace_QuotedPhrase_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case E — a bare token whose first char is '-' signals websearch negation intent,
    // so the whole query is passed through untouched.
    [Theory]
    [Trait("prd-case", "E")]
    [InlineData("pupil -deleted", "pupil -deleted")]
    public void OrJoinWhitespace_LeadingHyphenNegation_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case I — whitespace-only input is NOT trimmed to empty; string.IsNullOrWhiteSpace
    // short-circuits before the Trim() call, so the original whitespace string is echoed
    // back. Empty input echoes empty. Pin the OBSERVED behaviour — the PRD is silent on
    // pre-trim shape here (Landmine L-J).
    [Theory]
    [Trait("prd-case", "I")]
    [InlineData("   ", "   ")]
    [InlineData("", "")]
    public void OrJoinWhitespace_WhitespaceOrEmpty_ObservedBehaviour(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case I — tab / newline / carriage-return count as split separators alongside space,
    // so a tab-separated or CR/LF-separated query behaves identically to a space-separated
    // one under the bare multi-word join.
    [Theory]
    [Trait("prd-case", "I")]
    [InlineData("a\tb", "a OR b")]
    [InlineData("a\nb\rc", "a OR b OR c")]
    public void OrJoinWhitespace_TabAndNewlineSeparators_SplitAndJoin(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }

    // Case J — Unicode words (accented, non-ASCII) pass through with case preserved.
    // OrJoinWhitespace is case-preserving; it does NOT lowercase the input.
    [Theory]
    [Trait("prd-case", "J")]
    [InlineData("café", "café")]
    public void OrJoinWhitespace_UnicodeWord_PreservesCase(string input, string expected)
    {
        Assert.Equal(expected, SearchTermNormalizer.OrJoinWhitespace(input));
    }
}
