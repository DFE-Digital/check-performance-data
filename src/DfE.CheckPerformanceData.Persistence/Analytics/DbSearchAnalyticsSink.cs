using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using SearchEventEntity = DfE.CheckPerformance.Persistence.Entities.SearchEvent;
using SearchEventResultEntity = DfE.CheckPerformance.Persistence.Entities.SearchEventResult;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Postgres implementation of the search-analytics sink:
//
//   * RecordBatchAsync buffers every DTO in the batch as a SearchEvent parent + a set of
//     SearchEventResult children under a single SaveChangesAsync, so a batch of 100 rows
//     costs one insert round-trip's worth of latency instead of 100. The child rows use
//     the navigation-property pattern (child.SearchEvent = parent) so EF's change tracker
//     back-fills the parent's serial id before flushing — no post-write id fetch needed.
//   * PurgeExpiredAsync runs a raw-SQL CTE-batched DELETE (10 000 rows per DELETE, looped
//     until zero-affected) rather than the ExecuteDeleteAsync one-shot the metrics sink
//     uses. Research §4.3 flagged the one-shot as a landmine on the search_events table
//     at 100k+ rows: Postgres takes an ACCESS EXCLUSIVE lock and holds it for the whole
//     DELETE, stalling readers. Batching to 10k gives Postgres frequent commit points and
//     autovacuum a chance to reclaim tuples between batches. FK cascade on
//     search_event_results.search_event_id cleans up children automatically.
public sealed class DbSearchAnalyticsSink : ISearchAnalyticsSink
{
    // The purge batch size — every DELETE inside PurgeExpiredAsync deletes at most this
    // many rows before yielding. 10 000 keeps the lock window well under one second on
    // an 8-core Azure Flexible Server tier and lets autovacuum interleave.
    private const int PurgeBatchSize = 10_000;

    private readonly IPortalDbContext _dbContext;

    public DbSearchAnalyticsSink(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RecordBatchAsync(
        IReadOnlyList<SearchEventDto> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        // Two-phase insert: parent rows first so Postgres populates their serial ids,
        // then child rows referencing those ids. SearchEventConfiguration declares the
        // FK without a navigation property, so EF cannot back-fill child.SearchEventId
        // from an in-memory parent reference — we have to observe the assigned id after
        // the parent SaveChanges. The trade-off is two round-trips per batch instead of
        // one; the sink still writes a whole batch of N rows in constant round-trip cost
        // rather than 2N, which is the batching invariant.
        var parents = new List<(SearchEventEntity Entity, IReadOnlyList<SearchEventResultDto> Results)>(events.Count);
        foreach (var dto in events)
        {
            var parent = new SearchEventEntity
            {
                OccurredAtUtc = dto.OccurredAtUtc,
                SessionId = dto.SessionId,
                QueryRaw = dto.QueryRaw,
                QueryNormalised = dto.QueryNormalised,
                Scope = dto.Scope,
                ResultsPages = dto.ResultsPages,
                ResultsBlocks = dto.ResultsBlocks,
                LatencyMs = dto.LatencyMs,
            };
            _dbContext.SearchEvents.Add(parent);
            parents.Add((parent, dto.Results));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var anyChildren = false;
        foreach (var (parent, results) in parents)
        {
            foreach (var result in results)
            {
                _dbContext.SearchEventResults.Add(new SearchEventResultEntity
                {
                    SearchEventId = parent.Id,
                    Position = result.Position,
                    ResultKind = result.ResultKind,
                    ResultKey = result.ResultKey,
                    Rank = result.Rank,
                });
                anyChildren = true;
            }
        }
        if (anyChildren)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> PurgeExpiredAsync(
        TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var totalDeleted = 0;

        // CTE-batched DELETE loop: SELECT up to PurgeBatchSize expired row ids in a CTE,
        // then DELETE only those ids. Repeat until a batch touches zero rows. Postgres
        // FK cascade takes care of search_event_results children with the same batching
        // shape under the hood.
        while (!cancellationToken.IsCancellationRequested)
        {
            var affected = await _dbContext.Database.ExecuteSqlRawAsync(
                @"WITH victims AS (
                      SELECT id FROM search_events
                      WHERE occurred_at_utc < {0}
                      LIMIT " + PurgeBatchSize + @"
                  )
                  DELETE FROM search_events WHERE id IN (SELECT id FROM victims);",
                new object[] { cutoff },
                cancellationToken);

            if (affected <= 0)
            {
                break;
            }

            totalDeleted += affected;

            if (affected < PurgeBatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }
}
