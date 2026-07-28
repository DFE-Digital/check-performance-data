using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// EF-Core implementation of the seed-sample-search-data messages gateway. Adds each
// BackdatedSearchMessage to the change tracker and flushes in one SaveChanges. Kept
// deliberately separate from DbSearchMessageService so the (server-owned SubmittedAtUtc)
// invariant on the user-facing service is not diluted by a backdate-friendly overload.
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
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
