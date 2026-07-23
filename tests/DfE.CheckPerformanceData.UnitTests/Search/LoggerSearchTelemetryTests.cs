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
    private readonly ILogger<LoggerSearchTelemetry> _logger =
        Substitute.For<ILogger<LoggerSearchTelemetry>>();
    private readonly SearchZeroResultsCounter _counter = new();
    private readonly LoggerSearchTelemetry _sut;

    public LoggerSearchTelemetryTests()
    {
        _sut = new LoggerSearchTelemetry(_logger, _counter);
    }

    // ── Sample-event factory ────────────────────────────────────────────────────
    //
    // Small builder with sensible defaults so each fact overrides only what it needs.
    // Corpus-appropriate rank fields are populated as if the row came from PageNode
    // (RankKeywords/Title/Subtitle/Body) — RankValue stays null for page hits.

    private static SearchHitEvent SampleHit(
        string corpus = "page",
        string? rowId = null,
        string url = "/help/getting-started",
        string title = "Getting started",
        float rankTotal = 0.15f)
    {
        return new SearchHitEvent(
            Corpus: corpus,
            RowId: rowId ?? Guid.NewGuid().ToString(),
            Url: url,
            Title: title,
            RankTotal: rankTotal,
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
    // downstream log queries can filter on it as a structured property.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_EmitsInfoSummaryWithAllExpectedFields()
    {
        var evt = BuildEvent(
            hits:
            [
                SampleHit(corpus: "page", rowId: "page-1"),
                SampleHit(corpus: "page", rowId: "page-2"),
                SampleHit(corpus: "block", rowId: "block-1"),
            ],
            exclusions: [SampleExclusion()]);

        _sut.RecordSearch(evt);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("SearchId") &&
                o.ToString()!.Contains("ResultsPages") &&
                o.ToString()!.Contains("ResultsBlocks") &&
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

    // Per-hit Debug fan-out — exactly one Debug line per kept hit, carrying the rank
    // breakdown placeholders. Three hits in, three Debug calls out.
    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public void RecordSearch_WithHits_EmitsPerHitDebugLineForEachHit()
    {
        var evt = BuildEvent(hits:
        [
            SampleHit(rowId: "hit-1"),
            SampleHit(rowId: "hit-2"),
            SampleHit(rowId: "hit-3"),
        ]);

        _sut.RecordSearch(evt);

        _logger.Received(3).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("hit") &&
                o.ToString()!.Contains("rank_total")),
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
}
