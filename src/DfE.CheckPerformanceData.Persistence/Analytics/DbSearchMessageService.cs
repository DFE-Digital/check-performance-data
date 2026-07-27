using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Postgres implementation of the search-messages service. Owns messages-only writes and
// deletes — the events sink deliberately does not expose a messages-purge method
// (two owners for one table would drift out of sync). This concrete currently implements
// PurgeExpiredMessagesAsync; later work adds CreateAsync and the admin inbox reads /
// mark-read / session-purge.
//
// PurgeExpiredMessagesAsync uses the same CTE-batched DELETE loop as DbSearchAnalyticsSink
// for the same reason: at 10k+ rows a single-shot ExecuteDeleteAsync takes an ACCESS
// EXCLUSIVE lock long enough to stall the messages-inbox unread-count badge and the
// admin listing. The 10k batch bound keeps each lock window well under one second.
public sealed class DbSearchMessageService : ISearchMessageService
{
    // Purge batch size — matches the events sink's cadence for symmetry.
    private const int PurgeBatchSize = 10_000;

    private readonly IPortalDbContext _dbContext;

    public DbSearchMessageService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> PurgeExpiredMessagesAsync(
        TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var affected = await _dbContext.Database.ExecuteSqlRawAsync(
                @"WITH victims AS (
                      SELECT id FROM search_messages
                      WHERE submitted_at_utc < {0}
                      LIMIT " + PurgeBatchSize + @"
                  )
                  DELETE FROM search_messages WHERE id IN (SELECT id FROM victims);",
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
