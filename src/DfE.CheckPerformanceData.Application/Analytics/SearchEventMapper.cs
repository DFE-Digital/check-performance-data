using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.Analytics;

// Pure transform from the request-side SearchTelemetryEvent + session id to the
// persist-side (SearchEventDto, per-result rows) pair the sink writer emits. Static so
// the decorator can call it without a DI hop; takes the session id as a parameter so it
// can be unit-tested without an HttpContext.
//
// Per-hit resolution rule: each SearchHitEvent maps to exactly one persisted result row.
// A hit with any block contributor (BlockContributorCount > 0) resolves as a block —
// the block's key is the ResultKey and it counts toward ResultsBlocks. Otherwise it
// resolves as a page — the URL is the ResultKey and it counts toward ResultsPages.
// Rank carries the hit's aggregate RankTotal verbatim so the dashboard's rank sort
// matches the /search view ordering.
public static class SearchEventMapper
{
    public static (SearchEventDto Event, IReadOnlyList<SearchEventResultDto> Results) From(
        SearchTelemetryEvent evt, string sessionId)
    {
        var hitCount = evt.Hits.Count;

        if (hitCount == 0)
        {
            var empty = Array.Empty<SearchEventResultDto>();
            var emptyDto = new SearchEventDto(
                OccurredAtUtc: evt.UtcTimestamp,
                SessionId: sessionId,
                QueryRaw: evt.QueryRaw,
                QueryNormalised: evt.QueryNormalised,
                Scope: evt.Scope,
                ResultsPages: 0,
                ResultsBlocks: 0,
                LatencyMs: (int)evt.LatencyMsTotal,
                Results: empty);
            return (emptyDto, empty);
        }

        var results = new List<SearchEventResultDto>(hitCount);
        var pages = 0;
        var blocks = 0;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = evt.Hits[i];
            if (hit.BlockContributorCount > 0)
            {
                blocks++;
                // First contributing block key names the row; the parent SearchEvent
                // still carries the raw + normalised query so support can trace back.
                var key = hit.ContributingBlockKeys.Count > 0
                    ? hit.ContributingBlockKeys[0]
                    : hit.Url;
                results.Add(new SearchEventResultDto(
                    Position: i + 1,
                    ResultKind: "block",
                    ResultKey: key,
                    Rank: hit.RankTotal));
            }
            else
            {
                pages++;
                results.Add(new SearchEventResultDto(
                    Position: i + 1,
                    ResultKind: "page",
                    ResultKey: hit.Url,
                    Rank: hit.RankTotal));
            }
        }

        var dto = new SearchEventDto(
            OccurredAtUtc: evt.UtcTimestamp,
            SessionId: sessionId,
            QueryRaw: evt.QueryRaw,
            QueryNormalised: evt.QueryNormalised,
            Scope: evt.Scope,
            ResultsPages: pages,
            ResultsBlocks: blocks,
            LatencyMs: (int)evt.LatencyMsTotal,
            Results: results);
        return (dto, results);
    }
}
