using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// EF-Core implementation of the seed-sample-search-data messages gateway. Adds each
// BackdatedSearchMessage to the change tracker and flushes in one SaveChanges. Kept
// deliberately separate from DbSearchMessageService so the (server-owned SubmittedAtUtc)
// invariant on the user-facing service is not diluted by a backdate-friendly overload.
//
// Also owns the Danger-zone delete surface: DeleteSeededAsync filters on the is_seeded
// marker so real events survive; DeleteAllAsync drops every row regardless of marker.
// Both wrap their three-table deletes in ExecuteInTransactionAsync so a partial failure
// cannot leave the sink in a torn state (e.g. events deleted but child result rows
// still present would break the FK invariant that search_event_results.search_event_id
// always references an existing search_events.id).
public sealed class DbSampleSearchDataGateway : ISampleSearchDataGateway
{
    private readonly IPortalDbContext _dbContext;

    public DbSampleSearchDataGateway(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteBackdatedMessagesAsync(
        IReadOnlyList<BackdatedSearchMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var msg in messages)
        {
            _dbContext.SearchMessages.Add(new SearchMessage
            {
                SessionId = msg.SessionId,
                SubmittedAtUtc = msg.SubmittedAtUtc,
                WhatLookingFor = msg.WhatLookingFor,
                WhatGot = msg.WhatGot,
                Email = msg.Email,
                IsRead = false,
                // Every message written through this backdated write-path is a seeder
                // row by definition — real user-submitted messages go through
                // DbSearchMessageService.CreateAsync which does not touch this flag.
                IsSeeded = true,
                // Per-run marker so the rollback can drop exactly this seed's messages.
                JobId = msg.JobId,
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DeleteCountsResult> DeleteSeededAsync(CancellationToken cancellationToken)
    {
        var counts = new int[3];
        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            // Delete child result rows first so the FK cascade never has a chance to
            // fire (would be equivalent, but the explicit child-first delete keeps the
            // returned counts unambiguous — a cascade delete on the parent would return
            // 0 from the child ExecuteDelete since the rows are already gone).
            counts[1] = await _dbContext.SearchEventResults
                .Where(x => x.IsSeeded)
                .ExecuteDeleteAsync(cancellationToken);
            counts[0] = await _dbContext.SearchEvents
                .Where(x => x.IsSeeded)
                .ExecuteDeleteAsync(cancellationToken);
            counts[2] = await _dbContext.SearchMessages
                .Where(x => x.IsSeeded)
                .ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

        return new DeleteCountsResult(
            EventsDeleted: counts[0],
            ResultsDeleted: counts[1],
            MessagesDeleted: counts[2]);
    }

    public async Task<DeleteCountsResult> DeleteAllAsync(CancellationToken cancellationToken)
    {
        var counts = new int[3];
        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            counts[1] = await _dbContext.SearchEventResults
                .ExecuteDeleteAsync(cancellationToken);
            counts[0] = await _dbContext.SearchEvents
                .ExecuteDeleteAsync(cancellationToken);
            counts[2] = await _dbContext.SearchMessages
                .ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

        return new DeleteCountsResult(
            EventsDeleted: counts[0],
            ResultsDeleted: counts[1],
            MessagesDeleted: counts[2]);
    }

    public async Task<DeleteCountsResult> DeleteByJobIdAsync(
        string jobId, CancellationToken cancellationToken)
    {
        // Guard against a caller passing a blank job id — an unfiltered delete on this
        // path would be catastrophic. The Cancel endpoint should never invoke us this
        // way, but the check is one line and turns a would-be data-loss bug into a
        // predictable no-op.
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return new DeleteCountsResult(0, 0, 0);
        }

        var counts = new int[3];
        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            // Children first so the returned count reflects rows actually removed by
            // this DELETE rather than by an FK cascade on the parent — the caller
            // shows the count to the admin in a "rolled back N rows" banner.
            counts[1] = await _dbContext.SearchEventResults
                .Where(x => x.JobId == jobId)
                .ExecuteDeleteAsync(cancellationToken);
            counts[0] = await _dbContext.SearchEvents
                .Where(x => x.JobId == jobId)
                .ExecuteDeleteAsync(cancellationToken);
            counts[2] = await _dbContext.SearchMessages
                .Where(x => x.JobId == jobId)
                .ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

        return new DeleteCountsResult(
            EventsDeleted: counts[0],
            ResultsDeleted: counts[1],
            MessagesDeleted: counts[2]);
    }
}
