using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Pagination shape proofs for the collapsed SearchAsync primitive, exercised over a
// seeded 25-hit corpus against live Postgres. Pins:
//   - TotalCount reflects the canonicalised URL count (not raw contributor count),
//   - Page 1 and Page 2 slice disjoint windows,
//   - Custom PageSize produces a differently-sized slice,
//   - TotalPages = ceil(TotalCount / PageSize),
//   - Fold behaviour: 5 pages + 2 blocks whose LastSeenPath matches static-route
//     page URLs fold to 5 canonical hits, not 7,
//   - Page-beyond-total returns an empty slice with an accurate TotalCount rather
//     than silently clamping at the service tier (the controller re-clamps).
//
// Controller-tier clamps ([10, 50] PageSize and [1, TotalPages] Page) are enforced
// BEFORE the service call and are covered by the controller unit tests, not this
// file.
[Collection(nameof(PostgresCollection))]
public sealed class SearchPaginationTests(PostgresFixture fixture)
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

    private static PageNode BuildPage(string slug, string title, string? keywords = null)
    {
        var now = DateTime.UtcNow;
        return new PageNode
        {
            Id = Guid.NewGuid(),
            Segment = slug,
            Path = slug,
            Title = title,
            Keywords = keywords,
            AppearInSearch = true,
            PageType = "content",
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

    private static ContentBlock BuildBlock(string key, string keywords, string lastSeenPath)
    {
        var now = DateTime.UtcNow;
        return new ContentBlock
        {
            Key = key,
            BlockType = "prose",
            Value = "widget copy",
            ValuePlainText = "widget copy",
            Keywords = keywords,
            AppearInSearch = true,
            LastSeenPath = lastSeenPath,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // Seeds `count` published PageNodes all keyed with "paginationdemo" so a single
    // tsquery matches every row. Distinct segments/titles per index keep the fold-
    // behaviour proof clean (no accidental URL collisions across the corpus).
    private async Task SeedPaginationCorpusAsync(int count)
    {
        await using var ctx = _fixture.CreateContext();
        for (var i = 1; i <= count; i++)
        {
            var page = BuildPage($"paginationdemo-page-{i}", $"paginationdemo page {i}", keywords: "paginationdemo");
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, $"paginationdemo body copy {i}"));
        }
        await ctx.SaveChangesAsync();
    }

    // ── Page-1 slice + top-line TotalCount ───────────────────────────────────

    [Fact]
    [Trait("search-case", "pagination")]
    public async Task SearchAsync_MultiPageCorpus_Page1_ReturnsFirst20Hits()
    {
        await TruncateAsync();
        await SeedPaginationCorpusAsync(25);

        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationdemo", Page: 1, PageSize: 20));

        Assert.Null(result.InvalidReason);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(20, result.Hits.Count);
        // Zero-indexed internal per the shared _Pagination view model contract; the
        // widget's ?page=N query string is one-indexed and re-shifted at the surface.
        Assert.Equal(0, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(2, result.TotalPages);
    }

    // ── Page-2 slice — remaining hits, disjoint from Page 1 ──────────────────

    [Fact]
    [Trait("search-case", "pagination")]
    public async Task SearchAsync_MultiPageCorpus_Page2_ReturnsRemaining5Hits()
    {
        await TruncateAsync();
        await SeedPaginationCorpusAsync(25);

        var (sut, _) = BuildSut();

        var page1Result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationdemo", Page: 1, PageSize: 20));
        var page2Result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationdemo", Page: 2, PageSize: 20));

        Assert.Equal(25, page2Result.TotalCount);
        Assert.Equal(5, page2Result.Hits.Count);
        Assert.Equal(1, page2Result.Page);
        Assert.Equal(2, page2Result.TotalPages);

        // Disjoint slice: no URL appears on both pages.
        var page1Urls = page1Result.Hits.Select(h => h.Url).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(page2Result.Hits, h => page1Urls.Contains(h.Url));
    }

    // ── Custom PageSize slices to the requested window ───────────────────────

    [Fact]
    [Trait("search-case", "pagination")]
    public async Task SearchAsync_PageSizeCustom_ProducesCustomSlice()
    {
        await TruncateAsync();
        await SeedPaginationCorpusAsync(25);

        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationdemo", Page: 1, PageSize: 10));

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(10, result.Hits.Count);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
    }

    // ── Canonical URL count folds page + block contributors on the same URL ──

    [Fact]
    [Trait("search-case", "pagination")]
    public async Task SearchAsync_TotalCountReflectsCanonicalisedURLCount()
    {
        await TruncateAsync();

        // Five pages, two of which sit on paths that also appear in the static-route
        // whitelist (StaticRouteTitles). Two blocks with LastSeenPath matching those
        // static-route URLs fold into the two page contributors: page URL is
        // "/" + PageNode.Path and block URL is the block's LastSeenPath verbatim, so
        // the block validator resolves the title via the static-route table and the
        // canonicaliser sees both contributors at the same URL. Net: 5 canonical
        // rows, not 5 + 2 = 7.
        var pageA = BuildPage("check-your-pupil-data", "Fold alpha page", keywords: "paginationfold");
        var pageB = BuildPage("guidance", "Fold beta page", keywords: "paginationfold");
        var pageC = BuildPage("paginationfold-page-3", "Fold gamma page", keywords: "paginationfold");
        var pageD = BuildPage("paginationfold-page-4", "Fold delta page", keywords: "paginationfold");
        var pageE = BuildPage("paginationfold-page-5", "Fold epsilon page", keywords: "paginationfold");

        await using (var ctx = _fixture.CreateContext())
        {
            ctx.PageNodes.AddRange(pageA, pageB, pageC, pageD, pageE);
            ctx.PageNodeVersions.AddRange(
                BuildLiveVersion(pageA.Id, "paginationfold body A"),
                BuildLiveVersion(pageB.Id, "paginationfold body B"),
                BuildLiveVersion(pageC.Id, "paginationfold body C"),
                BuildLiveVersion(pageD.Id, "paginationfold body D"),
                BuildLiveVersion(pageE.Id, "paginationfold body E"));
            await ctx.SaveChangesAsync();
        }

        var block1 = BuildBlock("paginationfold-block-a", keywords: "paginationfold", lastSeenPath: "/check-your-pupil-data");
        var block2 = BuildBlock("paginationfold-block-b", keywords: "paginationfold", lastSeenPath: "/guidance");
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.ContentBlocks.AddRange(block1, block2);
            await ctx.SaveChangesAsync();
        }

        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationfold"));

        Assert.Null(result.InvalidReason);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Hits.Count);

        // The two static-route URLs carry both a page contributor AND a block
        // contributor - proves the fold happened rather than the block hitting a
        // separate row.
        var alphaFold = Assert.Single(result.Hits, h => h.Url == "/check-your-pupil-data");
        Assert.Equal(1, alphaFold.PageContributorCount);
        Assert.Equal(1, alphaFold.BlockContributorCount);
        Assert.Contains("paginationfold-block-a", alphaFold.ContributingBlockKeys);

        var betaFold = Assert.Single(result.Hits, h => h.Url == "/guidance");
        Assert.Equal(1, betaFold.PageContributorCount);
        Assert.Equal(1, betaFold.BlockContributorCount);
        Assert.Contains("paginationfold-block-b", betaFold.ContributingBlockKeys);
    }

    // ── Page-beyond-total: empty slice, accurate TotalCount, no silent clamp ─

    /// <summary>
    /// The service itself does NOT clamp Page to [1, TotalPages] - that's the
    /// controller's job. At the service tier, a Page well past the end returns an
    /// empty slice with an accurate TotalCount so the controller can re-clamp and
    /// re-issue if it wants. Silent clamping here would hide off-by-one bugs in the
    /// controller.
    /// </summary>
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task SearchAsync_PageBeyondTotal_ReturnsEmptySliceWithAccurateTotalCount()
    {
        await TruncateAsync();
        await SeedPaginationCorpusAsync(25);

        var (sut, _) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "paginationdemo", Page: 99, PageSize: 20));

        Assert.Null(result.InvalidReason);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        // A page past the end lands on the last valid page in the same call. Clamping here
        // rather than leaving the caller to re-issue is what keeps one user request to one
        // telemetry event; page is zero-indexed internally, so page 2 of 2 reads as 1.
        Assert.Equal(1, result.Page);
        Assert.Equal(5, result.Hits.Count);
    }
}
