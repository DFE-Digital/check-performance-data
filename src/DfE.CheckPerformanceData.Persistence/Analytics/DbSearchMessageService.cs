using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Skeleton Postgres implementation of the search-messages service. PurgeExpiredMessagesAsync
// returns 0 for now — the real batched CTE delete lands alongside the retention job in
// the emission plan. Registered scoped in DependencyManager so the storage-foundation
// plan can satisfy consumer construction; subsequent plans extend this concrete with
// CreateAsync (feedback form) and inbox reads / mark-read / session purge (admin inbox).
public sealed class DbSearchMessageService : ISearchMessageService
{
    private readonly IPortalDbContext _dbContext;

    public DbSearchMessageService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> PurgeExpiredMessagesAsync(TimeSpan olderThan, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
