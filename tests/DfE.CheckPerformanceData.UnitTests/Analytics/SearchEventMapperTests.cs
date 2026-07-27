using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Contract tests for SearchEventMapper — the static transform that projects a
// SearchTelemetryEvent + session id into the (SearchEventDto, IReadOnlyList<SearchEventResultDto>)
// pair the sink writer persists. Three shape facts:
//
//   * An event with no hits yields ResultsPages=0, ResultsBlocks=0 and an empty result list,
//     without allocating a per-hit list. Timestamp + query + scope round-trip verbatim.
//   * An event carrying a pure-page hit AND a hybrid block hit (BlockContributorCount > 0)
//     produces exactly one row per hit, ResultsPages=1 (the pure page) + ResultsBlocks=1
//     (the hybrid — the rule is "any block contributor makes the hit resolve as a block").
//   * Per-result rows are 1-indexed by Position in hit order, ResultKind is "page" or "block",
//     ResultKey is the URL for a page hit and the first contributing block key for a block
//     hit, and Rank carries the hit's RankTotal verbatim.
//
// The mapper takes the session id as a string parameter — it deliberately does not touch
// HttpContext so it can be unit-tested without a request in flight. The decorator that
// wraps it is responsible for reading Session.Id and handing it in.
public sealed class SearchEventMapperTests
{
    private static SearchHitEvent Hit(
        string url = "/help/getting-started",
        float rankTotal = 0.15f,
        bool pageContributed = true,
        int blockContributorCount = 0,
        IReadOnlyList<string>? contributingBlockKeys = null)
    {
        return new SearchHitEvent(
            Url: url,
            Title: "sample",
            RankTotal: rankTotal,
            PageContributed: pageContributed,
            BlockContributorCount: blockContributorCount,
            ContributingBlockKeys: contributingBlockKeys ?? [],
            RankKeywords: null,
            RankTitle: null,
            RankSubtitle: null,
            RankBody: null,
            RankValue: null);
    }

    private static SearchTelemetryEvent Event(
        DateTime? utcTimestamp = null,
        string queryRaw = "widget",
        string queryNormalised = "widget",
        string? scope = null,
        long latencyMsTotal = 42L,
        IReadOnlyList<SearchHitEvent>? hits = null)
    {
        return new SearchTelemetryEvent(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: utcTimestamp ?? DateTime.UtcNow,
            QueryRaw: queryRaw,
            QueryNormalised: queryNormalised,
            Scope: scope,
            LatencyMsTotal: latencyMsTotal,
            LatencyMsPages: 20L,
            LatencyMsBlocks: 15L,
            Hits: hits ?? [],
            FilterExclusions: []);
    }

    [Fact]
    public void From_EventWithNoHits_ProducesZeroCountsAndEmptyResultList()
    {
        var ts = new DateTime(2026, 07, 27, 12, 34, 56, DateTimeKind.Utc);
        var evt = Event(
            utcTimestamp: ts,
            queryRaw: "asdfghjkl",
            queryNormalised: "asdfghjkl",
            scope: "/help",
            latencyMsTotal: 7L,
            hits: []);

        var (dto, results) = SearchEventMapper.From(evt, "session-xyz");

        Assert.Equal(ts, dto.OccurredAtUtc);
        Assert.Equal("session-xyz", dto.SessionId);
        Assert.Equal("asdfghjkl", dto.QueryRaw);
        Assert.Equal("asdfghjkl", dto.QueryNormalised);
        Assert.Equal("/help", dto.Scope);
        Assert.Equal(0, dto.ResultsPages);
        Assert.Equal(0, dto.ResultsBlocks);
        Assert.Equal(7, dto.LatencyMs);
        Assert.Empty(dto.Results);
        Assert.Empty(results);
    }

    // A pure-page hit + a hybrid (block contributor count > 0) hit — the hybrid resolves as
    // a block per the plan rule ("each hit is counted once; a hybrid resolves as a block
    // hit"). The DTO's per-corpus counts must match, and the per-result list must carry
    // one row per hit — never per-contributor.
    [Fact]
    public void From_EventWithPureAndHybridHits_CountsHybridAsBlockAndYieldsOneResultPerHit()
    {
        var evt = Event(hits:
        [
            Hit(url: "/pages/one", rankTotal: 0.20f, pageContributed: true, blockContributorCount: 0),
            Hit(url: "/pages/two", rankTotal: 0.10f, pageContributed: true,
                blockContributorCount: 2, contributingBlockKeys: ["block-a", "block-b"]),
        ]);

        var (dto, results) = SearchEventMapper.From(evt, "session-abc");

        Assert.Equal(1, dto.ResultsPages);
        Assert.Equal(1, dto.ResultsBlocks);
        Assert.Equal(2, results.Count);
        // Position is 1-indexed, kind matches the resolution rule, key matches URL for
        // page hits and the first contributing block key for block hits.
        Assert.Equal(1, results[0].Position);
        Assert.Equal("page", results[0].ResultKind);
        Assert.Equal("/pages/one", results[0].ResultKey);
        Assert.Equal(0.20f, results[0].Rank);
        Assert.Equal(2, results[1].Position);
        Assert.Equal("block", results[1].ResultKind);
        Assert.Equal("block-a", results[1].ResultKey);
        Assert.Equal(0.10f, results[1].Rank);
    }

    // The full round-trip pin: three hits, one pure-page, one hybrid, one pure-block. Every
    // per-result row's Position, ResultKind, ResultKey, and Rank is asserted so a mapping
    // regression can only be a wholesale rewrite. The dto.Results collection must equal
    // the second element of the returned tuple exactly.
    [Fact]
    public void From_EventWithThreeMixedHits_PopulatesResultsListVerbatim()
    {
        var evt = Event(hits:
        [
            Hit(url: "/a", rankTotal: 0.9f, pageContributed: true, blockContributorCount: 0),
            Hit(url: "/b", rankTotal: 0.5f, pageContributed: true,
                blockContributorCount: 1, contributingBlockKeys: ["block-hybrid"]),
            Hit(url: "/c", rankTotal: 0.3f, pageContributed: false,
                blockContributorCount: 3, contributingBlockKeys: ["block-c-1", "block-c-2", "block-c-3"]),
        ]);

        var (dto, results) = SearchEventMapper.From(evt, "s");

        Assert.Equal(3, results.Count);
        Assert.Equal(1, dto.ResultsPages);
        Assert.Equal(2, dto.ResultsBlocks);

        Assert.Equal(1, results[0].Position);
        Assert.Equal("page", results[0].ResultKind);
        Assert.Equal("/a", results[0].ResultKey);
        Assert.Equal(0.9f, results[0].Rank);

        Assert.Equal(2, results[1].Position);
        Assert.Equal("block", results[1].ResultKind);
        Assert.Equal("block-hybrid", results[1].ResultKey);
        Assert.Equal(0.5f, results[1].Rank);

        Assert.Equal(3, results[2].Position);
        Assert.Equal("block", results[2].ResultKind);
        Assert.Equal("block-c-1", results[2].ResultKey);
        Assert.Equal(0.3f, results[2].Rank);

        Assert.Same(results, dto.Results);
    }
}
