using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// SuggestAsync backs the instant-search widget's typeahead. It runs against the real corpus
// on every keystroke, so two properties matter and are pinned here:
//
//   1. It sees exactly what SearchAsync sees. A suggestion that pointed at a page the real
//      search refuses to return would take the visitor somewhere they could not otherwise
//      reach, so the same scope rules and the same silent filters have to apply.
//   2. It records nothing. SearchAsync is the single place a SearchEvent row is written, and
//      a per-keystroke writer would bury the 90-day analytics store in noise and skew every
//      volume, latency and zero-result chart on the dashboard.
[Collection(nameof(PostgresCollection))]
public sealed class SiteSearchSuggestTests(PostgresFixture fixture)
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

    private FakeSearchTelemetry _telemetry = new();

    private SiteSearchService BuildSut()
    {
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);
        _telemetry = new FakeSearchTelemetry();
        return new SiteSearchService(
            pageRepo,
            blockSearch,
            _telemetry,
            new SearchResultCanonicaliser(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
    }

    private static PageNode BuildPage(string path, string title, string? keywords = null, bool appearInSearch = true)
    {
        var now = DateTime.UtcNow;
        var segments = path.Split('/');
        return new PageNode
        {
            Id = Guid.NewGuid(),
            Segment = segments[^1],
            Path = path,
            Title = title,
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            PageType = "content",
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private async Task SeedPagesAsync(params (PageNode Page, string Body)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        foreach (var (page, body) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(new PageNodeVersion
            {
                Id = Guid.NewGuid(),
                PageNodeId = page.Id,
                VersionId = 1,
                MinorVersion = 0,
                IsCurrent = true,
                Content = "[]",
                BodyPlainText = body,
                CreatedDate = now,
                UpdatedDate = now,
            });
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    [Trait("search-case", "single-word")]
    public async Task SuggestAsync_ReturnsMatchingPages_AsLabelAndUrl()
    {
        await TruncateAsync();
        var page = BuildPage("guidance/merging", "Merging pupil records", keywords: "merge");
        await SeedPagesAsync((page, "How to merge two records together."));

        var suggestions = await BuildSut().SuggestAsync(new SiteSearchSuggestQuery("merge"));

        var hit = Assert.Single(suggestions, s => s.Url == "/guidance/merging");
        Assert.Equal("Merging pupil records", hit.Label);
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SuggestAsync_Scope_RestrictsToDescendantsOfThatPath()
    {
        await TruncateAsync();
        var inScope = BuildPage("guidance/merging", "Merging in guidance", keywords: "merge");
        var outOfScope = BuildPage("help/merging", "Merging in help", keywords: "merge");
        await SeedPagesAsync((inScope, "merge"), (outOfScope, "merge"));

        var suggestions = await BuildSut().SuggestAsync(new SiteSearchSuggestQuery("merge", ScopePath: "guidance"));

        Assert.Contains(suggestions, s => s.Url == "/guidance/merging");
        Assert.DoesNotContain(suggestions, s => s.Url == "/help/merging");
    }

    [Fact]
    [Trait("search-case", "editor-suppressed")]
    public async Task SuggestAsync_HonoursTheSameSilentFilters_AsSearch()
    {
        // A suggestion that pointed at an AppearInSearch=false page would offer a route the
        // real search refuses to give.
        await TruncateAsync();
        var visible = BuildPage("guidance/visible-merge", "Visible merge", keywords: "merge");
        var suppressed = BuildPage("guidance/hidden-merge", "Hidden merge", keywords: "merge", appearInSearch: false);
        await SeedPagesAsync((visible, "merge"), (suppressed, "merge"));

        var suggestions = await BuildSut().SuggestAsync(new SiteSearchSuggestQuery("merge"));

        Assert.Contains(suggestions, s => s.Url == "/guidance/visible-merge");
        Assert.DoesNotContain(suggestions, s => s.Url == "/guidance/hidden-merge");
    }

    [Fact]
    [Trait("search-case", "very-short")]
    public async Task SuggestAsync_BelowMinimumLength_ReturnsEmpty()
    {
        await TruncateAsync();
        await SeedPagesAsync((BuildPage("guidance/merging", "Merging"), "merge"));

        Assert.Empty(await BuildSut().SuggestAsync(new SiteSearchSuggestQuery("m")));
    }

    [Fact]
    public async Task SuggestAsync_HonoursTheRequestedLimit()
    {
        await TruncateAsync();
        var rows = Enumerable.Range(0, 8)
            .Select(i => (BuildPage($"guidance/merge-{i}", $"Merge {i}", keywords: "merge"), "merge"))
            .ToArray();
        await SeedPagesAsync(rows);

        var suggestions = await BuildSut().SuggestAsync(new SiteSearchSuggestQuery("merge", Limit: 3));

        Assert.Equal(3, suggestions.Count);
    }

    [Fact]
    public async Task SuggestAsync_RecordsNoTelemetry()
    {
        await TruncateAsync();
        await SeedPagesAsync((BuildPage("guidance/merging", "Merging", keywords: "merge"), "merge"));

        var sut = BuildSut();
        await sut.SuggestAsync(new SiteSearchSuggestQuery("merge"));

        Assert.Empty(_telemetry.Events);
    }
}
