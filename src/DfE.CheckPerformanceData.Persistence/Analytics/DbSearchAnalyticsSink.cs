using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Skeleton Postgres implementation of the search-analytics sink. RecordBatchAsync throws
// NotImplementedException on purpose — the storage-foundation plan lands the interface
// + entity + migration, and the emission plan replaces this body with the real
// AddRange/SaveChanges batch write. PurgeExpiredAsync returns 0 for the same reason.
// Registered scoped in DependencyManager so consumer construction resolves cleanly
// today; the retention job that actually calls PurgeExpiredAsync lands alongside the
// real body in the next plan.
public sealed class DbSearchAnalyticsSink : ISearchAnalyticsSink
{
    private readonly IPortalDbContext _dbContext;

    public DbSearchAnalyticsSink(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task RecordBatchAsync(IReadOnlyList<SearchEventDto> events, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "DbSearchAnalyticsSink.RecordBatchAsync is a skeleton — the emission plan replaces this with the real batch write.");

    public Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
