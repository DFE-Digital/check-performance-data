using DfE.CheckPerformanceData.Application.Search;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract tests for LoggerSearchTelemetry — the default ISearchTelemetry implementation
// that fans one SearchTelemetryEvent out to three log tiers:
//
//   Info    — one summary line per request (all mandatory summary fields present).
//   Debug   — one line per kept hit (rank breakdown) and one line per exclusion (breadcrumb).
//   Warn    — zero-result line, with the zero-results counter Incremented alongside.
//
// The counter injection is a real SearchZeroResultsCounter instance (not a substitute) —
// the counter is trivial to construct fresh per test and reading the resulting count
// through the interface is a stronger assertion than "did we call Increment on the mock".
// The logger IS a substitute so we can inspect the recorded Log calls' level and message
// state without a real log sink in the loop.
//
// The two new [Trait("search-case", ...)] slugs — zero-result-telemetry and
// rank-breakdown-telemetry — are load-bearing: a downstream coverage meta-test enumerates
// them off method-level trait metadata, so class-level traits do not count. Slug mapping:
//
//   zero-result-telemetry     — behaviour tied to the counter + Warn path (empty hits).
//   rank-breakdown-telemetry  — behaviour tied to the summary + per-hit + per-exclusion
//                               fan-out (how ranking data flows into structured log
//                               properties, including the log-injection escape shape).
public sealed class LoggerSearchTelemetryTests
{
    // Tiny fake for ISearchDebugOptions so the existing facts can construct the SUT
    // with the debug toggle explicitly OFF (their assertions expect LogDebug per-hit
    // and per-exclusion lines). The extra facts below build a second SUT with the
    // toggle ON and assert those lines get promoted to LogInformation.
    private sealed class FakeSearchDebugOptions : ISearchDebugOptions
    {
        public bool ShowSearchDebug { get; init; }
    }

    private readonly ILogger<LoggerSearchTelemetry> _logger =
        Substitute.For<ILogger<LoggerSearchTelemetry>>();
    private readonly SearchZeroResultsCounter _counter = new();
    private readonly LoggerSearchTelemetry _sut;

    public LoggerSearchTelemetryTests()
    {
        _sut = new LoggerSearchTelemetry(_logger, _counter,
            new FakeSearchDebugOptions { ShowSearchDebug = false });
    }

    // ── Sample-event factory ────────────────────────────────────────────────────
    //
    // Small builder with sensible defaults so each fact overrides only what it needs.
    // Default winning contributor is the page — RankKeywords/Title/Subtitle/Body populated,
    // RankValue null. Set pageContributed=false + blockContributorCount>=1 to model a
    // pure-block-sourced canonical row (the caller usually flips the per-field ranks too).
    private static SearchHitEvent SampleHit(
        string url = "/help/getting-started",
        string title = "Getting started",
        float rankTotal = 0.15f,
        bool pageContributed = true,
        int blockContributorCount = 0,
        IReadOnlyList<string>? contributingBlockKeys = null)
    {
        return new SearchHitEvent(
            Url: url,
            Title: title,
            RankTotal: rankTotal,
            PageContributed: pageContributed,
            BlockContributorCount: blockContributorCount,
            ContributingBlockKeys: contributingBlockKeys ?? [],
            RankKeywords: 0.05f,
            RankTitle: 0.05f,
            RankSubtitle: 0.02f,
            RankBody: 0.03f,
            RankValue: null);
    }

    private static FilterExclusion SampleExclusion(
        string corpus = "block",
        string kind = "admin-path",
        string rowKey = "some-block-key")
    {
        return new FilterExclusion(Corpus: corpus, Kind: kind, RowKey: rowKey);
    }

    private static SearchTelemetryEvent BuildEvent(
        string queryRaw = "widget",
        string queryNormalised = "widget",
        string? scope = null,
        long latencyMsTotal = 42L,
        long? latencyMsPages = 25L,
        long? latencyMsBlocks = 17L,
        IReadOnlyList<SearchHitEvent>? hits = null,
        IReadOnlyList<FilterExclusion>? exclusions = null)
    {
        return new SearchTelemetryEvent(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: DateTime.UtcNow,
            QueryRaw: queryRaw,
            QueryNormalised: queryNormalised,
            Scope: scope,
            LatencyMsTotal: latencyMsTotal,
            LatencyMsPages: latencyMsPages,
            LatencyMsBlocks: latencyMsBlocks,
            Hits: hits ?? [],
            FilterExclusions: exclusions ?? []);
    }

    // ── Facts ────────────────────────────────────────────────────────────────────

    // Info-level summary line, one per request. Every mandatory field carried by
    // SearchTelemetryEvent must appear as a PascalCase placeholder in the template so
    // downstream log queries can filter on it as a structured property. The per-corpus
    // ResultsPages/ResultsBlocks pair collapses to a single ResultsHits placeholder — the
    // /search view now renders per-canonical rows, not per-contributor rows, so the count
    // that matters is the number of URLs the user saw.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_EmitsInfoSummaryWithAllExpectedFields()
    {
        var evt = BuildEvent(
            hits:
            [
                SampleHit(url: "/url-a"),
                SampleHit(url: "/url-b"),
                SampleHit(url: "/url-c", pageContributed: false, blockContributorCount: 1,
                          contributingBlockKeys: ["block-1"]),
            ],
            exclusions: [SampleExclusion()]);

        _sut.RecordSearch(evt);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("SearchId") &&
                o.ToString()!.Contains("ResultsHits") &&
                o.ToString()!.Contains("FilterExclusionsCount") &&
                o.ToString()!.Contains("LatencyMsTotal") &&
                o.ToString()!.Contains("LatencyMsPages") &&
                o.ToString()!.Contains("LatencyMsBlocks")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Zero-hit path: counter must move from 0 → 1 AND a Warn line must fire carrying the
    // three fields a support engineer needs to diagnose a zero-hit query.
    [Fact]
    [Trait("search-case", "zero-result-telemetry")]
    public void RecordSearch_WhenHitsEmpty_EmitsWarnAndIncrementsCounter()
    {
        var evt = BuildEvent(queryRaw: "asdfghjkl", queryNormalised: "asdfghjkl", scope: "/help");

        var before = _counter.Read();
        _sut.RecordSearch(evt);
        var after = _counter.Read();

        Assert.Equal(0L, before);
        Assert.Equal(1L, after);
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("QueryRaw") &&
                o.ToString()!.Contains("QueryNormalised") &&
                o.ToString()!.Contains("Scope")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Non-zero path: counter must NOT move and the Warn line must NOT fire. The Info
    // summary line still fires (asserted elsewhere) — this fact pins the counter contract
    // specifically.
    [Fact]
    [Trait("search-case", "zero-result-telemetry")]
    public void RecordSearch_WhenHitsNonEmpty_EmitsInfoAndCounterUnchanged()
    {
        var evt = BuildEvent(hits: [SampleHit()]);

        _sut.RecordSearch(evt);

        Assert.Equal(0L, _counter.Read());
        _logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Per-hit Debug fan-out — exactly one Debug line per kept canonical hit, carrying the
    // per-canonical contributor breakdown + winning-contributor rank placeholders. Three
    // hits in, three Debug calls out. New placeholders {PageContributed},
    // {BlockContributorCount}, {ContributingBlockKeys} sit alongside the pre-existing
    // per-field rank set — the whole template is asserted through its human-readable
    // labels rather than the raw brace expression so a placeholder-only rename still
    // trips this fact.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_WithHits_EmitsPerHitDebugLineForEachHit()
    {
        var evt = BuildEvent(hits:
        [
            SampleHit(url: "/hit-1"),
            SampleHit(url: "/hit-2"),
            SampleHit(url: "/hit-3", pageContributed: false, blockContributorCount: 2,
                      contributingBlockKeys: ["k1", "k2"]),
        ]);

        _sut.RecordSearch(evt);

        _logger.Received(3).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("hit") &&
                o.ToString()!.Contains("rank_total") &&
                o.ToString()!.Contains("page_contrib") &&
                o.ToString()!.Contains("block_count") &&
                o.ToString()!.Contains("block_keys")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Per-exclusion Debug fan-out — exactly one Debug line per filter exclusion. Two
    // exclusions in, two Debug calls out, distinct from the per-hit lines by template.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_WithExclusions_EmitsPerExclusionDebugLine()
    {
        var evt = BuildEvent(exclusions:
        [
            SampleExclusion(kind: "admin-path", rowKey: "admin-block-a"),
            SampleExclusion(kind: "e2e-key", rowKey: "e2e-block-b"),
        ]);

        _sut.RecordSearch(evt);

        _logger.Received(2).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("excluded") &&
                o.ToString()!.Contains("by")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Log-injection escape pin: the raw query, carrying an embedded newline + a bogus
    // "INFO:" prefix, MUST reach the log sink as a discrete structured argument (via a
    // {QueryRaw} placeholder), NOT concatenated into the template. Serilog's JSON
    // formatter escapes newlines / control characters on the argument side; assembling
    // the string with + or interpolation would let the injected line render as a fresh
    // log entry. Asserting that state.ToString() contains the substituted raw value is
    // the observable proof that the placeholder path was taken.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_QueryRawWithNewline_JsonEscapesNewline()
    {
        var raw = "line1\nINFO: injected";
        var evt = BuildEvent(queryRaw: raw, queryNormalised: raw, hits: [SampleHit()]);

        _sut.RecordSearch(evt);

        _logger.Received().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains(raw)),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Debug-toggle ON: per-hit rank breakdowns are promoted from Debug to Information so
    // operators can see them without lowering the process-wide log level. Three hits in,
    // three Information calls out carrying the per-canonical hit template.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_WhenShowSearchDebugTrue_EmitsPerHitAtInfoLevel()
    {
        var logger = Substitute.For<ILogger<LoggerSearchTelemetry>>();
        var sut = new LoggerSearchTelemetry(logger, new SearchZeroResultsCounter(),
            new FakeSearchDebugOptions { ShowSearchDebug = true });
        var evt = BuildEvent(hits:
        [
            SampleHit(url: "/hit-1"),
            SampleHit(url: "/hit-2"),
            SampleHit(url: "/hit-3", pageContributed: false, blockContributorCount: 1,
                      contributingBlockKeys: ["k1"]),
        ]);

        sut.RecordSearch(evt);

        logger.Received(3).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("hit") &&
                o.ToString()!.Contains("rank_total") &&
                o.ToString()!.Contains("page_contrib") &&
                o.ToString()!.Contains("block_count")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
        logger.DidNotReceive().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("hit") &&
                o.ToString()!.Contains("rank_total")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Debug-toggle ON: per-exclusion filter breadcrumbs promoted from Debug to
    // Information for the same reason. Two exclusions in, two Information calls out.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_WhenShowSearchDebugTrue_EmitsPerExclusionAtInfoLevel()
    {
        var logger = Substitute.For<ILogger<LoggerSearchTelemetry>>();
        var sut = new LoggerSearchTelemetry(logger, new SearchZeroResultsCounter(),
            new FakeSearchDebugOptions { ShowSearchDebug = true });
        var evt = BuildEvent(exclusions:
        [
            SampleExclusion(kind: "admin-path", rowKey: "admin-block-a"),
            SampleExclusion(kind: "e2e-key", rowKey: "e2e-block-b"),
        ]);

        sut.RecordSearch(evt);

        logger.Received(2).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("excluded") &&
                o.ToString()!.Contains("by")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
        logger.DidNotReceive().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("excluded") &&
                o.ToString()!.Contains("by")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
