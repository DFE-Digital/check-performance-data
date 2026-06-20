using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Observability;

// Deletes synthetic demo traffic from the three pipeline tables, matched by the well-known demo
// reference prefixes, while leaving real submissions untouched. queue_metrics_events carries the
// reference in its own column; queue_dead_letters and queue_messages carry it inside the JSON
// payload (`{"Reference":"…"}`), so those two match on the payload. Set-based ExecuteDeleteAsync
// per prefix per table — no rows are loaded into memory. Dev/test only (the gated endpoint guards
// it); the prefix list is a fixed internal constant, never caller input.
public sealed class DemoTrafficPurger : IDemoTrafficPurger
{
    private readonly IPortalDbContext _dbContext;

    public DemoTrafficPurger(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DemoPurgeResult> PurgeAsync(CancellationToken cancellationToken)
    {
        var metricEvents = 0;
        var deadLetters = 0;
        var queueMessages = 0;

        foreach (var prefix in DemoTrafficPrefixes.All)
        {
            // The reference column (metrics) matches on the prefix; the JSON payload (dead-letters
            // and queue messages) matches the embedded `"Reference":"<prefix>…"`.
            var refPattern = prefix + "%";
            var payloadPattern = "%\"Reference\":\"" + prefix + "%";

            metricEvents += await _dbContext.QueueMetricEvents
                .Where(e => EF.Functions.ILike(e.ReferenceNumber, refPattern))
                .ExecuteDeleteAsync(cancellationToken);

            deadLetters += await _dbContext.DeadLetters
                .Where(d => EF.Functions.ILike(d.Payload, payloadPattern))
                .ExecuteDeleteAsync(cancellationToken);

            queueMessages += await _dbContext.QueueMessages
                .Where(m => EF.Functions.ILike(m.Payload, payloadPattern))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return new DemoPurgeResult(metricEvents, deadLetters, queueMessages);
    }
}
