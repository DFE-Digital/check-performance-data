using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// End-to-end telemetry-emission harness. Composes SiteSearchService over the real
// PageNodeRepository + ContentBlockSearchService + ContentBlockRepository against a live
// Testcontainers Postgres, seeds fixtures that trip each of the seven silent-filter slugs
// plus a known-hit corpus and a zero-result corpus, and asserts the captured
// SearchTelemetryEvent field-by-field.
//
// FakeSearchTelemetry captures the event so the assertion body can read raw
// SearchTelemetryEvent fields without parsing log output. LoggerSearchTelemetry is
// exercised by the unit-level LoggerSearchTelemetryTests — its job here would only be to
// route to Serilog, which duplicates coverage the unit tests already own.
//
// Companion coverage meta-test lives at tests/DfE.CheckPerformanceData.UnitTests/Search/
// SearchCaseCoverageTests.cs and asserts each traited slug on these facts contributes to
// the documented "at least one test per behaviour" invariant.
[Collection(nameof(PostgresCollection))]
public sealed class SearchTelemetryE2ETests(PostgresFixture fixture)
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

    // Compose the search graph exactly as production wires it, plus a fake telemetry sink
    // and a real zero-results counter. Returned as a tuple so tests can assert on the SUT
    // response, the captured event, and the counter's Read() value from a single call.
    private (SiteSearchService Sut, FakeSearchTelemetry Fake, SearchZeroResultsCounter Counter) BuildSut()
    {
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        // Pass-through: search flow only needs Value → plain-text so BuildSnippet has
        // something to slice. Real HTML sanitisation is out of scope here.
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);
        var fake = new FakeSearchTelemetry();
        var counter = new SearchZeroResultsCounter();
        var sut = new SiteSearchService(pageRepo, blockSearch, fake);
        return (sut, fake, counter);
    }

    private static ContentBlock BuildBlock(
        string key,
        string keywords,
        string lastSeenPath,
        bool appearInSearch = true,
        string valuePlainText = "widget copy")
    {
        var now = DateTime.UtcNow;
        return new ContentBlock
        {
            Key = key,
            BlockType = "prose",
            Value = valuePlainText,
            ValuePlainText = valuePlainText,
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            LastSeenPath = lastSeenPath,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static PageNode BuildPage(
        string slug,
        string title,
        string? keywords = null,
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
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            PageType = pageType,
            DeletedDate = deletedDate,
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private static PageNodeVersion BuildLiveVersion(Guid pageId, string bodyPlainText, bool isCurrent = true)
    {
        var now = DateTime.UtcNow;
        return new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = pageId,
            VersionId = 1,
            MinorVersion = isCurrent ? 0 : 1,
            IsCurrent = isCurrent,
            Content = "[]",
            BodyPlainText = bodyPlainText,
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private async Task SeedBlocksAsync(params ContentBlock[] blocks)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var b in blocks) ctx.ContentBlocks.Add(b);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedPagesAsync(params (PageNode Page, string Body, bool IsCurrent)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var (page, body, isCurrent) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, body, isCurrent));
        }
        await ctx.SaveChangesAsync();
    }

    // ── Silent-filter emission proofs (one fact per slug) ─────────────────────

    [Fact]
    [Trait("search-filter", "admin-path")]
    public async Task SearchAsync_AdminPathBlock_EmitsAdminPathExclusionInEvent()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-admin", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("admin-widget-trip", keywords: "widget", lastSeenPath: "/admin/pages/edit");
        await SeedBlocksAsync(control, suppressed);

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.ContentBlockHits, h => h.Key == "visible-widget-admin");
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "block" && e.Kind == "admin-path" && e.RowKey == "admin-widget-trip");
    }

    [Fact]
    [Trait("search-filter", "e2e-key")]
    public async Task SearchAsync_E2eKeyBlock_EmitsE2eKeyExclusionInEvent()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-e2e", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("e2e-widget-trip", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        await SeedBlocksAsync(control, suppressed);

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.ContentBlockHits, h => h.Key == "visible-widget-e2e");
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "block" && e.Kind == "e2e-key" && e.RowKey == "e2e-widget-trip");
    }

    [Fact]
    [Trait("search-filter", "guidance-ks4-2026-nav-key")]
    public async Task SearchAsync_GuidanceNavBlock_EmitsGuidanceKs4NavExclusionInEvent()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-nav", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("guidance-ks4-2026-nav", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        await SeedBlocksAsync(control, suppressed);

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.ContentBlockHits, h => h.Key == "visible-widget-nav");
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "block" && e.Kind == "guidance-ks4-2026-nav-key" && e.RowKey == "guidance-ks4-2026-nav");
    }

    [Fact]
    [Trait("search-filter", "contentblock-appearinsearch-false")]
    public async Task SearchAsync_HiddenContentBlock_EmitsContentBlockAppearInSearchFalseExclusionInEvent()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-cb", keywords: "widget", lastSeenPath: "/check-your-pupil-data", appearInSearch: true);
        var suppressed = BuildBlock("hidden-widget-trip", keywords: "widget", lastSeenPath: "/check-your-pupil-data", appearInSearch: false);
        await SeedBlocksAsync(control, suppressed);

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.ContentBlockHits, h => h.Key == "visible-widget-cb");
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "block" && e.Kind == "contentblock-appearinsearch-false" && e.RowKey == "hidden-widget-trip");
    }

    [Fact]
    [Trait("search-filter", "pagenode-appearinsearch-false")]
    public async Task SearchAsync_HiddenPageNode_EmitsPagenodeAppearInSearchFalseExclusionInEvent()
    {
        await TruncateAsync();
        var visible = BuildPage("widget-visible-page", "Widget visible page", keywords: "widget", appearInSearch: true);
        var hidden = BuildPage("widget-hidden-page", "Widget hidden page", keywords: "widget", appearInSearch: false);
        await SeedPagesAsync((visible, "body", true), (hidden, "body", true));

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.PageHits, h => h.PageId == visible.Id);
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "page" && e.Kind == "pagenode-appearinsearch-false" && e.RowKey == "widget-hidden-page");
    }

    [Fact]
    [Trait("search-filter", "draft-page")]
    public async Task SearchAsync_DraftPageNode_EmitsDraftPageExclusionInEvent()
    {
        await TruncateAsync();
        var published = BuildPage("widget-published-page", "Widget published page", keywords: "widget");
        var draft = BuildPage("widget-draft-page", "Widget draft page", keywords: "widget");
        await SeedPagesAsync((published, "body", true), (draft, "body", false));

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.PageHits, h => h.PageId == published.Id);
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "page" && e.Kind == "draft-page" && e.RowKey == "widget-draft-page");
    }

    [Fact]
    [Trait("search-filter", "unpublished-target")]
    public async Task SearchAsync_OrphanContentBlock_EmitsUnpublishedTargetExclusionInEvent()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-orphan", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("orphan-widget-trip", keywords: "widget", lastSeenPath: "/unpublished-target-nowhere");
        await SeedBlocksAsync(control, suppressed);

        var (sut, fake, _) = BuildSut();
        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.ContentBlockHits, h => h.Key == "visible-widget-orphan");
        var evt = Assert.Single(fake.Events);
        Assert.Contains(evt.FilterExclusions,
            e => e.Corpus == "block" && e.Kind == "unpublished-target" && e.RowKey == "orphan-widget-trip");
    }

    // ── Per-field rank breakdown on kept hits ─────────────────────────────────

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_KeptPageHit_CarriesFourPerFieldRanksInEvent()
    {
        await TruncateAsync();
        // One visible page + one visible block, both matching "widget" — so evt.Hits carries
        // one entry per corpus and the assertion can prove the page corpus null-on-RankValue
        // asymmetry against the block corpus null-on-RankTitle/Subtitle/Body asymmetry.
        var page = BuildPage("widget-rank-page", "Widget rank page", keywords: "widget");
        await SeedPagesAsync((page, "widget appears in body", true));
        var block = BuildBlock("widget-rank-block", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        await SeedBlocksAsync(block);

        var (sut, fake, _) = BuildSut();
        await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        var evt = Assert.Single(fake.Events);
        Assert.Equal(2, evt.Hits.Count);

        var pageHit = Assert.Single(evt.Hits, h => h.Corpus == "page");
        Assert.NotNull(pageHit.RankKeywords);
        Assert.NotNull(pageHit.RankTitle);
        Assert.NotNull(pageHit.RankSubtitle);
        Assert.NotNull(pageHit.RankBody);
        Assert.Null(pageHit.RankValue);

        var blockHit = Assert.Single(evt.Hits, h => h.Corpus == "block");
        Assert.NotNull(blockHit.RankKeywords);
        Assert.NotNull(blockHit.RankValue);
        Assert.Null(blockHit.RankTitle);
        Assert.Null(blockHit.RankSubtitle);
        Assert.Null(blockHit.RankBody);
    }

    // ── Zero-result branch: empty Hits + counter side-effect ──────────────────

    [Fact]
    [Trait("search-case", "zero-result-telemetry")]
    public async Task SearchAsync_ZeroResultQuery_IncrementsCounterAndEmitsEmptyHits()
    {
        await TruncateAsync();
        // Seed nothing — the query matches no row. FakeSearchTelemetry stands in for the
        // production LoggerSearchTelemetry at emission capture, but the counter side-effect
        // lives on the sink itself. Compose a real LoggerSearchTelemetry over the real
        // counter, wire IT into the SUT, and run the search — then read the counter.
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);

        var counter = new SearchZeroResultsCounter();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggerSearchTelemetry>.Instance;
        var loggerSink = new LoggerSearchTelemetry(logger, counter);

        // Also compose a fake so the assertion body can read the emitted event fields.
        // Fan the emission out to both sinks via a small pass-through.
        var fake = new FakeSearchTelemetry();
        var fanout = new FanoutTelemetry(loggerSink, fake);

        var sut = new SiteSearchService(pageRepo, blockSearch, fanout);

        Assert.Equal(0L, counter.Read());
        await sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        var evt = Assert.Single(fake.Events);
        Assert.Empty(evt.Hits);
        Assert.Equal(1L, counter.Read());
    }

    // Test-only fan-out sink: routes one RecordSearch call to both a real
    // LoggerSearchTelemetry (which bumps the injected counter) and a capture fake (which
    // stores the event for field assertions). Lives inside this test class because the
    // zero-result fact is the only site that needs both the counter side-effect AND raw
    // event access — a project-scoped helper would be over-engineering.
    private sealed class FanoutTelemetry(ISearchTelemetry primary, ISearchTelemetry secondary) : ISearchTelemetry
    {
        public void RecordSearch(SearchTelemetryEvent evt)
        {
            primary.RecordSearch(evt);
            secondary.RecordSearch(evt);
        }
    }
}
