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
}
