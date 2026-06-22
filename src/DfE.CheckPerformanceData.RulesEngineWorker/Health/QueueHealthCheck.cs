using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Health;

/// <summary>
/// Readiness probe for the worker's data plane: confirms the database is reachable and
/// that a cheap queue poll succeeds. A worker that cannot reach its queue store cannot
/// make progress, so this reports unhealthy and lets the orchestrator restart it rather
/// than leaving a stuck process running.
///
/// Responses carry status only — no connection strings, queue payloads or pupil data —
/// so an exposed health port discloses nothing beyond reachability.
/// </summary>
public sealed class QueueHealthCheck : IHealthCheck
{
    private readonly PortalDbContext _dbContext;
    private readonly IQueueService _queueService;

    public QueueHealthCheck(PortalDbContext dbContext, IQueueService queueService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Database is unreachable.");
            }

            await _queueService.GetQueueDepthsAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database reachable and queue poll succeeded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Deliberately do not surface the exception message or data: it can carry the
            // connection string or other internal detail that must not leak over the probe.
            return HealthCheckResult.Unhealthy("Queue store is unreachable.");
        }
    }
}
