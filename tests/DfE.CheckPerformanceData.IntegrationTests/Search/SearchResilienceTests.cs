using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Resilience proofs for the collapsed SearchAsync primitive:
//
//   1. Hyphen-fallback service-tier scenarios - fires-and-rescues, fires-then-still-
//      zero, and operator-only-does-not-fallback - pin the trigger conditions on the
//      real DB (the unit-tier version can't observe the tsquery vs tsvector mismatch
//      that drives the fallback in the first place).
//
// The DB-killed proof (SearchAsync must return DataStoreUnavailable when Postgres is
// down) lives in the sibling SearchDbUnavailableTests class - it runs against its
// own private Testcontainers container so stopping the DB mid-test doesn't poison
// the shared PostgresCollection scope other integration tests depend on.
[Collection(nameof(PostgresCollection))]
public sealed class SearchResilienceTests(PostgresFixture fixture)
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

    private async Task SeedPagesAsync(params (PageNode Page, string Body)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var (page, body) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, body));
        }
        await ctx.SaveChangesAsync();
    }

    // -- Hyphen-fallback service-tier scenarios -------------------------------

    /// <summary>
    /// Fires-and-rescues: primary websearch_to_tsquery on "widget-assembly" misses the
    /// space-separated title "widget assembly" because the compound-lexeme phrase clause
    /// the parser emits doesn't hit the per-word tsvector. Zero primary hits + hyphen
    /// present + not operator-only -> fallback re-runs with hyphens replaced by spaces,
    /// which does match. The rescued result flows back and telemetry emits the fallback
    /// event (what the user experienced), not the primary.
    /// </summary>
    [Fact]
    [Trait("search-case", "numeric-hyphenated")]
    public async Task SearchAsync_HyphenatedQueryReturningPrimaryZero_TriggersFallback_ReturnsHits()
    {
        await TruncateAsync();
        var page = BuildPage("widget-assembly-guide", "widget assembly");
        await SeedPagesAsync((page, "widget assembly body copy"));

        var (sut, telemetry) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget-assembly"));

        Assert.Null(result.InvalidReason);
        Assert.Single(result.Hits);
        Assert.Contains("widget", result.Hits[0].Title, StringComparison.OrdinalIgnoreCase);
        // Exactly one telemetry emission - the fallback (which is what the user's
        // rescued response reflects). Primary emission is suppressed on rescue.
        Assert.Single(telemetry.Events);
    }

    /// <summary>
    /// Fires-then-still-zero: primary returns zero and fallback also returns zero. The
    /// service falls through to primary-event emission so the zero-result Warn from the
    /// sink still fires on the user's original query. Exactly one telemetry event
    /// captured - the fallback attempt does not double-emit.
    /// </summary>
    [Fact]
    [Trait("search-case", "numeric-hyphenated")]
    public async Task SearchAsync_HyphenatedQueryReturningPrimaryZero_AndFallbackAlsoZero_ReturnsZeroWithoutTelemetryDoubling()
    {
        await TruncateAsync();

        var (sut, telemetry) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "widget-assembly"));

        Assert.Null(result.InvalidReason);
        Assert.Empty(result.Hits);
        Assert.Single(telemetry.Events);
    }

    /// <summary>
    /// Operator-only guard: a query that carries a leading-hyphen negation is
    /// operator-only per the normaliser rules. Even though it contains a hyphen and
    /// returns zero hits, the fallback MUST NOT trigger - the user signalled intent
    /// via the operator and we must respect it. Exactly one telemetry event
    /// (the primary only - no fallback attempted).
    /// </summary>
    /// <remarks>
    /// A single "-" query would trip the two-character minimum-term-length guard
    /// upstream and emit zero events (invalid queries don't reach the backends).
    /// A two-character-or-longer operator-only hyphen query ("-widget") is the
    /// smallest input that actually exercises the fallback branch's operator-only
    /// guard while still emitting the primary event we assert on.
    /// </remarks>
    [Fact]
    [Trait("search-case", "operator-only")]
    public async Task SearchAsync_OperatorOnlyQueryContainingHyphen_DoesNotTriggerFallback()
    {
        await TruncateAsync();

        var (sut, telemetry) = BuildSut();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "-widget"));

        Assert.Null(result.InvalidReason);
        Assert.Empty(result.Hits);
        // Exactly one primary event; the operator-only guard suppresses the fallback
        // attempt entirely, so there is no second emission to worry about.
        Assert.Single(telemetry.Events);
    }
}

// Standalone DB-killed proof. Owns its own PostgreSqlContainer via IAsyncLifetime so
// that stopping the container mid-test doesn't leak into the shared PostgresCollection
// scope - subsequent integration tests in that collection depend on a live DB, and
// Testcontainers-dotnet's per-instance port mapping is not re-populated on
// stop-then-start of the same container instance (a known-limitation workaround, hence
// the private container).
//
// Wall-clock time on this test is inflated (~30-60s) by EnableRetryOnFailure's
// exponential back-off - the retrying execution strategy waits between attempts before
// the terminal DbException surfaces (wrapped in RetryLimitExceededException, which the
// service tier now handles). Do NOT disable EnableRetryOnFailure in production - Azure
// Flexible Server relies on it for transient connection flaps. This slow-category test
// can be filtered out of inner-loop runs via `dotnet test --filter "category!=slow"`.
public sealed class SearchDbUnavailableTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    /// <summary>
    /// Stops the private Postgres container BEFORE the SearchAsync call, then asserts
    /// the service returns a paged result flagged DataStoreUnavailable rather than
    /// throwing. The DbException catch inside SiteSearchService is the load-bearing
    /// guard here; a regression that lets it propagate would take the whole request
    /// down instead of rendering the graceful unavailable branch on the UI. Telemetry
    /// is expected to emit ZERO events on this branch - the contract is "per search
    /// request that reached the backends", and a killed-container run did not.
    /// </summary>
    [Fact]
    [Trait("search-case", "db-unavailable")]
    [Trait("category", "slow")]
    public async Task SearchAsync_WithKilledDatabaseContainer_ReturnsDataStoreUnavailable_WithoutThrowing()
    {
        // Compose the SUT while the container is still up.
        var ctx = CreateContext();
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

        // Stop the container - subsequent queries hit a closed socket, EnableRetryOnFailure
        // exhausts its retries, and the terminal DbException (wrapped in RetryLimit-
        // ExceededException) is caught inside SiteSearchService.
        await _postgres.StopAsync();

        var result = await sut.SearchAsync(new SiteSearchQuery(Query: "anytext"));

        Assert.Equal(SearchInvalidReason.DataStoreUnavailable, result.InvalidReason);
        Assert.Empty(result.Hits);
        Assert.Equal("anytext", result.CurrentQuery);
        Assert.Equal(0, result.TotalCount);
        // Killed-container branch emits zero telemetry - the sink only sees search
        // requests that actually reached the backends and completed.
        Assert.Empty(telemetry.Events);
    }
}
