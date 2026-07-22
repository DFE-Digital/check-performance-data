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

        var hits = await Repo().SearchAsync("widget", 10);

        Assert.Single(hits);
        Assert.Equal("visible-widget", hits[0].Key);
    }
}
