using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// Repository-tier FTS regression cases for ContentBlock corpus. Each [Fact]/[Theory] carries
// [Trait("search-case", <slug>)] so the coverage meta-test can enumerate them. Method-level
// traits only.
//
// Ranking assertions are relative-ordering only. Absolute ts_rank floats are corpus-dependent
// and must never be asserted.
[Collection(nameof(PostgresCollection))]
public sealed class ContentBlockRepositorySearchTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ContentBlockVersions"", ""ContentBlocks"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private ContentBlockRepository Repo() => new(_fixture.CreateContext());

    private static ContentBlock BuildBlock(
        string key,
        string valuePlainText,
        string? keywords = null,
        bool appearInSearch = true,
        string blockType = "prose")
    {
        var now = DateTime.UtcNow;
        return new ContentBlock
        {
            Key = key,
            BlockType = blockType,
            Value = valuePlainText,
            ValuePlainText = valuePlainText,
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task SeedAsync(params ContentBlock[] blocks)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var b in blocks) ctx.ContentBlocks.Add(b);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    [Trait("search-case", "numeric-hyphenated")]
    public async Task SearchAsync_QuotedNumberAndWordPhrase_MatchesAdjacentLexemes()
    {
        await TruncateAsync();
        var target = BuildBlock("guidance-hero-2026", "2026 guidance for the next window");
        var noise = BuildBlock("noise-block", "unrelated body text");
        await SeedAsync(target, noise);

        // "2026 guidance" is a quoted phrase → passes through the normaliser untouched
        // and Postgres builds `2026 <-> guidanc` (stemmed), matching only rows whose
        // tsvector has the two lexemes at adjacent positions.
        var hits = await Repo().SearchAsync("\"2026 guidance\"", 10);

        Assert.Contains(hits, h => h.Key == "guidance-hero-2026");
        Assert.DoesNotContain(hits, h => h.Key == "noise-block");
    }

    [Theory]
    [Trait("search-case", "special-chars")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE \"ContentBlocks\";--")]
    public async Task SearchAsync_MaliciousInput_DoesNotThrow_AndTableSurvives(string malicious)
    {
        await TruncateAsync();
        await SeedAsync(BuildBlock("harmless-block", "hello world"));

        var ex = await Record.ExceptionAsync(() => Repo().SearchAsync(malicious, 10));
        Assert.Null(ex);

        // Fresh context — proves the ContentBlocks table still exists and the seed row
        // is still there, i.e. the injection-shaped input never reached SQL as code.
        await using var ctx = _fixture.CreateContext();
        var remaining = await ctx.ContentBlocks.CountAsync();
        Assert.True(remaining > 0);
    }

    [Fact]
    [Trait("search-case", "duplicate-blocks")]
    public async Task SearchAsync_KeywordsMatchOnly_OutranksValueOnlyMatch()
    {
        await TruncateAsync();
        // Keywords is weight A; ValuePlainText is weight B. Relative ordering only per L-O.
        var keywordsHit = BuildBlock("alpha-block", string.Empty, keywords: "widget");
        var valueHit = BuildBlock("beta-block", "widget content");
        await SeedAsync(keywordsHit, valueHit);

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Equal(2, hits.Count);
        Assert.Equal("alpha-block", hits[0].Key);
        Assert.Equal("beta-block", hits[1].Key);
    }

    [Fact]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_BlockWithAppearInSearchFalse_IsExcluded()
    {
        await TruncateAsync();
        var visible = BuildBlock("visible-widget", "widget content", appearInSearch: true);
        var hidden = BuildBlock("hidden-widget", "widget content", appearInSearch: false);
        await SeedAsync(visible, hidden);

        // The repository now returns the hidden block tagged with an exclusion slug alongside
        // the kept row; the user-facing hit set is the kept subset. This test still pins the
        // "hidden block never surfaces in the kept subset" invariant.
        var hits = await Repo().SearchAsync("widget", 10);
        var kept = hits.Where(h => h.ExcludedBy == null).ToList();

        Assert.Single(kept);
        Assert.Equal("visible-widget", kept[0].Key);
    }

    // ── Widened-projection extension facts ─────────────────────────────────
    // The widened SearchAsync returns kept rows (ExcludedBy == null) alongside rows the
    // SQL-tier CASE tagged as excluded. Service-tier consumers apply a defensive filter
    // to drop excluded rows out of user-facing results; the next wave threads them into
    // telemetry. The facts below pin the widened shape: per-field ranks populated on
    // kept rows and exclusion slugs matching Phase 1.06's three block-corpus SQL-tier
    // filter slugs on the corresponding excluded rows.

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_KeptRowFromWidenedQuery_CarriesTwoPerFieldRankValues()
    {
        await TruncateAsync();
        var block = BuildBlock("widget-carrier", "widget in value", keywords: "widget");
        await SeedAsync(block);

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Single(hits);
        var row = hits[0];
        Assert.Null(row.ExcludedBy);
        Assert.NotNull(row.RankKeywords);
        Assert.True(row.RankKeywords!.Value > 0f, $"RankKeywords: {row.RankKeywords}");
        Assert.NotNull(row.RankValue);
    }

    [Fact]
    [Trait("search-filter", "e2e-key")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_E2eKeyPrefixBlock_ReturnsExcludedRow_TaggedWithE2eKey()
    {
        await TruncateAsync();
        var visible = BuildBlock("visible-widget-a", "widget content");
        var suppressed = BuildBlock("e2e-widget-a", "widget content");
        await SeedAsync(visible, suppressed);

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Equal(2, hits.Count);
        Assert.Single(hits, h => h.ExcludedBy == null && h.Key == "visible-widget-a");
        Assert.Single(hits, h => h.ExcludedBy == "e2e-key" && h.Key == "e2e-widget-a");
    }

    [Fact]
    [Trait("search-filter", "guidance-ks4-2026-nav-key")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_GuidanceNavBlock_ReturnsExcludedRow_TaggedWithGuidanceKs4Nav()
    {
        await TruncateAsync();
        var visible = BuildBlock("visible-widget-b", "widget content");
        var suppressed = BuildBlock("guidance-ks4-2026-nav", "widget content");
        await SeedAsync(visible, suppressed);

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Equal(2, hits.Count);
        Assert.Single(hits, h => h.ExcludedBy == null && h.Key == "visible-widget-b");
        Assert.Single(hits, h => h.ExcludedBy == "guidance-ks4-2026-nav-key" && h.Key == "guidance-ks4-2026-nav");
    }

    [Fact]
    [Trait("search-filter", "contentblock-appearinsearch-false")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_AppearInSearchFalseBlock_ReturnsExcludedRow_TaggedWithContentBlockAppearInSearchFalse()
    {
        await TruncateAsync();
        var visible = BuildBlock("visible-widget-c", "widget content", appearInSearch: true);
        var suppressed = BuildBlock("hidden-widget-c", "widget content", appearInSearch: false);
        await SeedAsync(visible, suppressed);

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Equal(2, hits.Count);
        Assert.Single(hits, h => h.ExcludedBy == null && h.Key == "visible-widget-c");
        Assert.Single(hits, h => h.ExcludedBy == "contentblock-appearinsearch-false" && h.Key == "hidden-widget-c");
    }
}
