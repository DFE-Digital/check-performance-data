using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// Repository-tier FTS regression cases. Each [Fact] carries [Trait("prd-case", <letter>)] so the
// PRD §6 case-matrix meta-test can enumerate coverage. Method-level traits only (class-level
// traits do not inherit unless the assertion opts in with inherit: true).
//
// Case K (duplicate-block dedup) is not exercised here — dedup lives in ContentBlockSearchService
// at the service tier and is covered by SiteSearchServiceFilterTests.
//
// Ranking assertions are relative-ordering only (Assert.Equal(topId, hits[0].PageId) or
// Assert.True(hits.IndexOf(a) < hits.IndexOf(b))). Absolute ts_rank floats are corpus-dependent
// and must never be asserted.
[Collection(nameof(PostgresCollection))]
public sealed class PageNodeRepositorySearchTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private PageNodeRepository Repo() => new(_fixture.CreateContext());

    private static PageNode BuildPage(
        string slug,
        string title,
        string? keywords = null,
        string? subtitle = null,
        bool appearInSearch = true,
        string pageType = "content",
        DateTime? deletedDate = null)
    {
        var now = DateTime.UtcNow;
        return new PageNode
        {
            Id = Guid.NewGuid(),
            Segment = slug,
            Path = slug,
            Title = title,
            Subtitle = subtitle,
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            PageType = pageType,
            DeletedDate = deletedDate,
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private static PageNodeVersion BuildLiveVersion(Guid pageId, string bodyPlainText)
    {
        var now = DateTime.UtcNow;
        return new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = pageId,
            VersionId = 1,
            MinorVersion = 0,
            IsCurrent = true,
            Content = "[]",
            BodyPlainText = bodyPlainText,
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private async Task SeedAsync(params (PageNode Page, string Body)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var (page, body) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, body));
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    [Trait("prd-case", "A")]
    public async Task SearchPagesAsync_SingleWordQuery_ReturnsMatchingPageAsTopHit()
    {
        await TruncateAsync();
        var mergePage = BuildPage("merge-records", "Merge pupil records");
        var otherPage = BuildPage("unrelated", "Unrelated topic");
        await SeedAsync((mergePage, "General guidance."), (otherPage, "Nothing to see."));

        var hits = await Repo().SearchPagesAsync("merge", null, 10);

        Assert.Single(hits);
        Assert.Equal(mergePage.Id, hits[0].PageId);
    }

    [Fact]
    [Trait("prd-case", "B")]
    public async Task SearchPagesAsync_MultiWordBareQuery_ReturnsUnionOfWordMatches()
    {
        await TruncateAsync();
        var mergePage = BuildPage("merge-alpha", "Merge alpha");
        var pupilPage = BuildPage("pupil-beta", "Pupil beta");
        await SeedAsync((mergePage, "General."), (pupilPage, "General."));

        // "merge pupil" is bare whitespace so the normaliser turns it into "merge OR pupil".
        var hits = await Repo().SearchPagesAsync("merge pupil", null, 10);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.PageId == mergePage.Id);
        Assert.Contains(hits, h => h.PageId == pupilPage.Id);
    }

    [Fact]
    [Trait("prd-case", "D")]
    public async Task SearchPagesAsync_QuotedPhraseQuery_ReturnsOnlyAdjacentLexemes()
    {
        await TruncateAsync();
        // Adjacent — pupil at position 1, records at position 2 in the title tsvector.
        var adjacent = BuildPage("pupil-records-overview", "Pupil records overview");
        // Non-adjacent — "and" is a stopword but its position is still counted so pupils is
        // at position 3 and records at position 1, no <-> match possible.
        var separated = BuildPage("records-and-pupils", "Records and pupils summary");
        await SeedAsync((adjacent, "Details."), (separated, "Details."));

        var hits = await Repo().SearchPagesAsync("\"pupil records\"", null, 10);

        Assert.Single(hits);
        Assert.Equal(adjacent.Id, hits[0].PageId);
    }

    [Fact]
    [Trait("prd-case", "E")]
    public async Task SearchPagesAsync_NegationQuery_ExcludesRowsMatchingNegatedTerm()
    {
        await TruncateAsync();
        var live = BuildPage("pupil-records-live", "Pupil records overview");
        var withDeleted = BuildPage("pupil-records-deleted-header", "Pupil records deleted");
        await SeedAsync((live, "General."), (withDeleted, "General."));

        // Leading "-" on the second token flags websearch negation — SearchTermNormalizer
        // passes the query through untouched. Postgres reads it as `pupil & !deleted`.
        var hits = await Repo().SearchPagesAsync("pupil -deleted", null, 10);

        Assert.Single(hits);
        Assert.Equal(live.Id, hits[0].PageId);
    }

    [Fact]
    [Trait("prd-case", "F")]
    public async Task SearchPagesAsync_SpaceSeparatedSlugWords_MatchesTitleTokens()
    {
        await TruncateAsync();
        var slugPage = BuildPage("check-your-pupil-data", "Check your pupil data");
        var noise = BuildPage("something-else", "Something else entirely");
        await SeedAsync((slugPage, "Overview."), (noise, "Overview."));

        var hits = await Repo().SearchPagesAsync("check your pupil data", null, 10);

        Assert.Contains(hits, h => h.PageId == slugPage.Id);
    }

    // websearch_to_tsquery('english', 'check-your-pupil-data') produces a compound-lexeme
    // phrase query that requires the hyphenated slug to be indexed as-is; the current
    // tsvector for "Check your pupil data" holds only the individual word lexemes, so the
    // whole query fails to match. Parked per D-08 as known baseline gap.
    [Fact(Skip = "Known baseline gap — case-f-slug")]
    [Trait("prd-case", "F")]
    [Trait("known-bug", "case-f-slug")]
    public async Task SearchPagesAsync_HyphenatedSlugQuery_MatchesTitleTokens()
    {
        await TruncateAsync();
        var slugPage = BuildPage("check-your-pupil-data", "Check your pupil data");
        var noise = BuildPage("something-else", "Something else entirely");
        await SeedAsync((slugPage, "Overview."), (noise, "Overview."));

        var hits = await Repo().SearchPagesAsync("check-your-pupil-data", null, 10);

        Assert.Contains(hits, h => h.PageId == slugPage.Id);
    }

    [Fact]
    [Trait("prd-case", "M")]
    public async Task SearchPagesAsync_KeywordsOnlyMatch_OutranksBodyOnlyMatch()
    {
        await TruncateAsync();
        // Keywords is weight A on the PageNode search vector. Body is weight D on the
        // PageNodeVersion vector. Combined ts_rank for a Keywords-only hit outranks a
        // Body-only hit — relative ordering only per Landmine L-O.
        var keywordsHit = BuildPage("alpha-page", "Alpha page", keywords: "widget");
        var bodyHit = BuildPage("beta-page", "Beta page");
        await SeedAsync((keywordsHit, string.Empty), (bodyHit, "widget content here"));

        var hits = await Repo().SearchPagesAsync("widget", null, 10);

        Assert.Equal(2, hits.Count);
        Assert.Equal(keywordsHit.Id, hits[0].PageId);
        Assert.Equal(bodyHit.Id, hits[1].PageId);
    }

    [Fact]
    [Trait("prd-case", "N")]
    public async Task SearchPagesAsync_SoftDeletedPage_IsExcludedFromResults()
    {
        await TruncateAsync();
        var live = BuildPage("kraken-topic", "Kraken topic");
        var deleted = BuildPage("kraken-clone", "Kraken clone", deletedDate: DateTime.UtcNow);
        await SeedAsync((live, "General."), (deleted, "General."));

        var hits = await Repo().SearchPagesAsync("kraken", null, 10);

        Assert.Single(hits);
        Assert.Equal(live.Id, hits[0].PageId);
    }
}
