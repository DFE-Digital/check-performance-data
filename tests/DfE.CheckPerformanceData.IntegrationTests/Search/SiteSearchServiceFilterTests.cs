using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Service-tier tests for the seven silent-filter invariants. Each [Fact] carries method-level
// [Trait("search-filter", <slug>)] and [Trait("search-case", <slug>)] so the case-coverage
// and filter-coverage meta-tests can enumerate coverage via either dimension. Method-level
// traits only (class-level traits do not inherit unless the assertion opts in with
// inherit: true).
//
// Each test seeds one "control" row (that should appear) and one "suppressed" row (that
// should be filtered). Both share the query term "widget" so the tsquery matches both at
// SQL level — the assertion proves the filter, not the FTS query.
//
// Filters live at different depths (admin-path and unpublished-target in the service tier,
// e2e-key / guidance-nav / AppearInSearch / draft-page at the SQL boundary). Testing them at
// the outermost service boundary catches removal at any depth.
[Collection(nameof(PostgresCollection))]
public sealed class SiteSearchServiceFilterTests(PostgresFixture fixture)
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

    // Manually compose the search graph — no existing integration test spins up
    // SiteSearchService end-to-end. Repositories and the block-search service share one
    // DbContext; the SUT reads through it.
    private SiteSearchService BuildSut()
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
        return new SiteSearchService(pageRepo, blockSearch, new FakeSearchTelemetry(), new SearchResultCanonicaliser(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
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

    // ── Block-tier filters ───────────────────────────────────────────────────

    [Fact]
    [Trait("search-filter", "admin-path")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_ContentBlockLastSeenOnAdminPath_IsExcluded()
    {
        await TruncateAsync();
        // Control resolves via the static-route table (StaticRouteTitles) so no PageNode
        // needs seeding to prove the block would otherwise surface.
        var control = BuildBlock("visible-widget-a", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("admin-widget-a", keywords: "widget", lastSeenPath: "/admin/pages/edit");
        await SeedBlocksAsync(control, suppressed);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.ContributingBlockKeys.Contains("visible-widget-a"));
        Assert.DoesNotContain(result.Hits, h => h.ContributingBlockKeys.Contains("admin-widget-a"));
    }

    [Fact]
    [Trait("search-filter", "e2e-key")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_ContentBlockWithE2eKeyPrefix_IsExcluded()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-b", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("e2e-widget-b", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        await SeedBlocksAsync(control, suppressed);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.ContributingBlockKeys.Contains("visible-widget-b"));
        Assert.DoesNotContain(result.Hits, h => h.ContributingBlockKeys.Contains("e2e-widget-b"));
    }

    [Fact]
    [Trait("search-filter", "guidance-ks4-2026-nav-key")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_GuidanceKs4NavContentBlock_IsExcluded()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-c", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("guidance-ks4-2026-nav", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        await SeedBlocksAsync(control, suppressed);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.ContributingBlockKeys.Contains("visible-widget-c"));
        Assert.DoesNotContain(result.Hits, h => h.ContributingBlockKeys.Contains("guidance-ks4-2026-nav"));
    }

    [Fact]
    [Trait("search-filter", "contentblock-appearinsearch-false")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_ContentBlockWithAppearInSearchFalse_IsExcluded()
    {
        await TruncateAsync();
        var control = BuildBlock("visible-widget-d", keywords: "widget", lastSeenPath: "/check-your-pupil-data", appearInSearch: true);
        var suppressed = BuildBlock("hidden-widget-d", keywords: "widget", lastSeenPath: "/check-your-pupil-data", appearInSearch: false);
        await SeedBlocksAsync(control, suppressed);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.ContributingBlockKeys.Contains("visible-widget-d"));
        Assert.DoesNotContain(result.Hits, h => h.ContributingBlockKeys.Contains("hidden-widget-d"));
    }

    [Fact]
    [Trait("search-filter", "unpublished-target")]
    [Trait("search-case", "unpublished-target")]
    public async Task SearchAsync_ContentBlockLastSeenPathPointsToUnpublishedPage_IsExcluded()
    {
        await TruncateAsync();
        // Control uses a static route (recognised without needing a PageNode row);
        // suppressed points at a path that neither matches a published PageNode nor
        // is in the static-route whitelist — the resolver returns null and the block
        // is dropped.
        var control = BuildBlock("visible-widget-e", keywords: "widget", lastSeenPath: "/check-your-pupil-data");
        var suppressed = BuildBlock("orphan-widget-e", keywords: "widget", lastSeenPath: "/unpublished-target-nowhere");
        await SeedBlocksAsync(control, suppressed);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.ContributingBlockKeys.Contains("visible-widget-e"));
        Assert.DoesNotContain(result.Hits, h => h.ContributingBlockKeys.Contains("orphan-widget-e"));
    }

    // ── Page-tier filters ────────────────────────────────────────────────────

    [Fact]
    [Trait("search-filter", "pagenode-appearinsearch-false")]
    [Trait("search-case", "editor-suppressed")]
    public async Task SearchAsync_PageNodeWithAppearInSearchFalse_IsExcluded()
    {
        await TruncateAsync();
        var visible = BuildPage("widget-visible", "Widget visible", keywords: "widget", appearInSearch: true);
        var hidden = BuildPage("widget-hidden", "Widget hidden", keywords: "widget", appearInSearch: false);
        await SeedPagesAsync((visible, "body", true), (hidden, "body", true));

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.Url == "/widget-visible" && h.PageContributorCount == 1);
        Assert.DoesNotContain(result.Hits, h => h.Url == "/widget-hidden");
    }

    [Fact]
    [Trait("search-filter", "draft-page")]
    [Trait("search-case", "unpublished-target")]
    public async Task SearchAsync_DraftOrSoftDeletedPageNode_IsExcluded()
    {
        await TruncateAsync();
        // Control: PageNode with an IsCurrent=true version — treated as published.
        // Suppressed: PageNode whose only version is IsCurrent=false — the SearchPagesAsync
        // .Where(x => x.Live != null) subquery drops it. Editors can leave a draft page in
        // place without it surfacing in /search.
        var published = BuildPage("widget-published", "Widget published", keywords: "widget");
        var draft = BuildPage("widget-draft", "Widget draft", keywords: "widget");
        await SeedPagesAsync((published, "body", true), (draft, "body", false));

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Contains(result.Hits, h => h.Url == "/widget-published" && h.PageContributorCount == 1);
        Assert.DoesNotContain(result.Hits, h => h.Url == "/widget-draft");
    }
}
