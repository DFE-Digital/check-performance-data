using System.Text;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Fuzz-safety proofs for the collapsed SearchAsync primitive: 30 curated hazards
// (control chars, RTL / bidi / invisible Unicode via numeric escape, backslash forms,
// HTML fragments, SQL fragments, combined character-set hazards) + four operator-only
// variants + a deterministic Random(42) x 1000-iteration character-set-mix generator.
// Every hazard must complete without throwing and must return a non-null result whose
// InvalidReason is anything BUT DataStoreUnavailable (a DB-unavailable result during
// fuzz signals the container died mid-run, which is worth surfacing loudly rather than
// silently accepting).
//
// IMPORTANT - file hygiene:
//   Do NOT paste literal RTL / LRE / RLE / PDF / LRO / RLO / BOM / ZWSP characters into
//   this file. The project's prompt-injection detector flags bare invisible codepoints
//   and the source review will fail. Reference every hazard by numeric \uXXXX escape or
//   char.ConvertFromUtf32(0xXXXX) so the C# source stays ASCII-safe.
[Collection(nameof(PostgresCollection))]
public sealed class SearchFuzzInputTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ContentBlockVersions"", ""ContentBlocks"", ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private (SiteSearchService Sut, FakeSearchTelemetry Telemetry) BuildSut()
    {
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);
        var telemetry = new FakeSearchTelemetry();
        var sut = new SiteSearchService(
            pageRepo,
            blockSearch,
            telemetry,
            new SearchResultCanonicaliser(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
        return (sut, telemetry);
    }

    // -- Curated hazard corpus ------------------------------------------------

    /// <summary>
    /// Thirty named hazards covering the specific character classes PRD Case J enumerates.
    /// Every invisible / bidi / control codepoint is spelled as a numeric \uXXXX escape;
    /// pasting the literal character would trip the source-review invisible-Unicode guard.
    /// </summary>
    public static IEnumerable<object[]> FuzzHazards()
    {
        // Control chars - every one is a single byte that Npgsql or Postgres may reject
        // outright (the null byte in particular). SearchAsync must degrade gracefully.
        yield return new object[] { "\0", "null-byte" };
        yield return new object[] { "\t", "tab" };
        yield return new object[] { "\n", "newline" };
        yield return new object[] { "\r", "carriage-return" };
        yield return new object[] { "\u0001", "soh" };
        yield return new object[] { "\u001F", "unit-separator" };

        // RTL / bidi / invisible Unicode - the class of characters that a malicious
        // author could hide inside CMS content to make search-result URLs render as
        // something they aren't. Every hazard MUST be spelled as a numeric \uXXXX escape
        // - pasting the literal character would trip the source-review invisible-Unicode
        // guard.
        yield return new object[] { "\u200E", "lrm" };
        yield return new object[] { "\u200F", "rlm" };
        yield return new object[] { "\u202A", "lre" };
        yield return new object[] { "\u202B", "rle" };
        yield return new object[] { "\u202C", "pdf" };
        yield return new object[] { "\u202D", "lro" };
        yield return new object[] { "\u202E", "rlo" };
        yield return new object[] { "\uFEFF", "bom" };
        yield return new object[] { "\u200B", "zwsp" };

        // Backslash forms - the escape-sequence class that JSON / SQL / regex parsers
        // fumble on. Postgres tsquery treats them as literal punctuation.
        yield return new object[] { "\\\\", "backslash" };
        yield return new object[] { "\\'", "backslash-single-quote" };
        yield return new object[] { "\\\"", "backslash-double-quote" };

        // HTML fragments - active-content payloads that must NOT be treated as HTML at
        // the search-input layer (they are search terms, not rendered content).
        yield return new object[] { "<script>alert(1)</script>", "html-script-tag" };
        yield return new object[] { "<img src=x onerror=y>", "html-img-onerror" };
        yield return new object[] { "<a href='javascript:alert(1)'>x</a>", "html-js-href" };

        // SQL fragments - the classic injection strings that end up in every SQL search
        // parser's test corpus. Parameterised Npgsql binds these as text - they should
        // never execute.
        yield return new object[] { "'; DROP TABLE ContentBlocks;--", "sql-drop-table" };
        yield return new object[] { "1' OR '1'='1", "sql-or-injection" };
        yield return new object[] { "1;SELECT * FROM PageNodes", "sql-select-injection" };

        // Combined character-set hazards - layer categories on top of each other.
        yield return new object[] { "<script>a</script>' OR 1=1--", "combined-html-sql" };
        yield return new object[] { "<script>a\0b</script>", "combined-html-null" };
        yield return new object[] { "text\0text", "combined-null-in-text" };

        // Punctuation-heavy / bare-token variants - cover the parse-side edge cases.
        yield return new object[] { "!@#$%^&*()_+={}|:<>?[]`~", "all-punctuation" };
        yield return new object[] { "\u2010\u2013\u2014\u2018\u2019\u201C\u201D", "unicode-punctuation" };
        yield return new object[] { "``` `` `", "backticks" };
    }

    /// <summary>
    /// The operator-only subset - every entry is either a bare websearch operator or a
    /// combination of operators with no content word. These MUST return zero hits (no
    /// content matches them) and MUST NOT trigger the hyphen-fallback.
    /// </summary>
    public static IEnumerable<object[]> OperatorOnlyHazards()
    {
        yield return new object[] { "OR", "operator-or" };
        yield return new object[] { "\"\"", "operator-empty-quotes" };
        yield return new object[] { "-", "operator-negation" };
        yield return new object[] { "OR OR", "operator-double-or" };
    }

    // -- Test A: curated hazards do not throw ---------------------------------

    /// <summary>
    /// Every curated hazard flows through the full pipeline (normaliser -> repositories
    /// -> canonicaliser -> paging) against a live but empty Postgres and returns a
    /// well-formed result. The assertion is deliberately weak: not-null and not-DB-
    /// unavailable. A stronger shape assertion (Hits.Count == 0, TotalCount == 0)
    /// would fail-close on any hazard that happens to match content - the goal here
    /// is fuzz safety, not corpus coverage.
    /// </summary>
    [Theory]
    [Trait("search-case", "fuzz-safety")]
    [MemberData(nameof(FuzzHazards))]
    public async Task SearchAsync_HazardInput_DoesNotThrow(string input, string caseName)
    {
        await TruncateAsync();
        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: input));

        Assert.NotNull(result);
        Assert.NotEqual(SearchInvalidReason.DataStoreUnavailable, result.InvalidReason);
        // caseName carries the human-readable slug - surfaces in the xUnit test-runner
        // output when a specific hazard regresses.
        _ = caseName;
    }

    // -- Test B: operator-only hazards return zero hits -----------------------

    /// <summary>
    /// Operator-only inputs are the "user typed pure syntax with no term" variants -
    /// these MUST return zero hits without any content match and MUST NOT trigger the
    /// hyphen-fallback. Stacks a second trait so the search-case coverage walk sees
    /// them under both slugs.
    /// </summary>
    [Theory]
    [Trait("search-case", "fuzz-safety")]
    [Trait("search-case", "operator-only")]
    [MemberData(nameof(OperatorOnlyHazards))]
    public async Task SearchAsync_OperatorOnlyInput_ReturnsZeroHitsWithoutThrowing(string input, string caseName)
    {
        await TruncateAsync();
        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: input));

        Assert.NotNull(result);
        Assert.NotEqual(SearchInvalidReason.DataStoreUnavailable, result.InvalidReason);
        Assert.Empty(result.Hits);
        _ = caseName;
    }

    // -- Test C: deterministic Random(42) x 1000 iterations do not throw ------

    /// <summary>
    /// Deterministic fuzz: seed 42, 1000 iterations, character-set-mix generator
    /// interleaves ASCII printable, Unicode punctuation, control chars, and operator
    /// tokens. Every iteration must complete without throwing and must return a
    /// non-null result whose InvalidReason is anything but DataStoreUnavailable
    /// (which would indicate the container died mid-run, poisoning subsequent
    /// iterations). Fixed seed means a regression names the specific iteration that
    /// broke, not just a runtime error at some unknown position.
    /// </summary>
    /// <remarks>
    /// Category=slow so inner-loop <c>dotnet test --filter "category!=slow"</c> runs
    /// skip this. Full-suite CI includes it. Wall-clock target is under 60s over the
    /// whole 1000-iteration loop (each iteration is a single SearchAsync against an
    /// empty corpus).
    /// </remarks>
    [Fact]
    [Trait("search-case", "fuzz-safety")]
    [Trait("category", "slow")]
    public async Task SearchAsync_DeterministicRandomInputs_DoNotThrowAcross1000Iterations()
    {
        await TruncateAsync();
        var (sut, _) = BuildSut();

        var rng = new Random(42);

        for (var i = 0; i < 1000; i++)
        {
            var input = GenerateFuzzString(rng);
            var result = await sut.SearchAsync(new SiteSearchQuery(Query: input));

            Assert.NotNull(result);
            // Include the iteration index and the exact input bytes in the failure
            // message so a regression can be reproduced with a targeted [InlineData].
            Assert.True(
                result.InvalidReason != SearchInvalidReason.DataStoreUnavailable,
                $"Iteration {i} produced DataStoreUnavailable - container likely died mid-run. Input bytes: [{FormatBytes(input)}]");
        }
    }

    /// <summary>
    /// Character-set-mix generator: length 1..40, each character position chosen from
    /// one of four buckets - ASCII printable (0x20..0x7E), Unicode punctuation
    /// (0x2010..0x2027), control chars (0x00..0x1F), or a whole operator token
    /// (<c>OR</c>, <c>"</c>, <c>-</c>). Deterministic when the caller supplies a
    /// fixed-seed Random.
    /// </summary>
    public static string GenerateFuzzString(Random rng)
    {
        var length = rng.Next(1, 41);
        var sb = new StringBuilder(length * 2);
        var operatorTokens = new[] { "OR", "\"", "-" };

        for (var i = 0; i < length; i++)
        {
            var bucket = rng.Next(4);
            switch (bucket)
            {
                case 0:
                    // ASCII printable
                    sb.Append((char)rng.Next(0x20, 0x7F));
                    break;
                case 1:
                    // Unicode general punctuation block
                    sb.Append((char)rng.Next(0x2010, 0x2028));
                    break;
                case 2:
                    // C0 control chars - includes null byte, tab, newline, CR, etc.
                    sb.Append((char)rng.Next(0x00, 0x20));
                    break;
                case 3:
                    // Whole operator token - inserts multi-char sequences at once.
                    sb.Append(operatorTokens[rng.Next(operatorTokens.Length)]);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string FormatBytes(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"U+{(int)c:X4}");
            if (sb.Length > 200)
            {
                sb.Append("...");
                break;
            }
        }
        return sb.ToString();
    }
}
