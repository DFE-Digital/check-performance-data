using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;

/// <summary>
/// Bounds the growth of the queue metrics events table. The queue work tables are deleted on
/// ack, so the events table is the only durable processing history and would otherwise grow
/// without limit. On each tick it purges event rows older than the settings-driven retention
/// window. Mirrors <see cref="DlqRetentionJob"/>'s loop and two-overload shape so the inner
/// purge can be driven directly in a test.
/// </summary>
public sealed class MetricsRetentionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsRetentionJob> _logger;
    private readonly TimeSpan _interval;

    public MetricsRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<MetricsRetentionJob> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromHours(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metrics retention tick failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RunOnceAsync(
            services.GetRequiredService<ISettingService>(),
            services.GetRequiredService<IMetricsSink>(),
            cancellationToken);

    public async Task RunOnceAsync(
        ISettingService settings,
        IMetricsSink metricsSink,
        CancellationToken cancellationToken)
    {
        var retentionDays = await settings.GetIntAsync(SettingKeys.MetricsRetentionDays);
        var purged = await metricsSink.PurgeExpiredAsync(TimeSpan.FromDays(retentionDays), cancellationToken);
        if (purged > 0)
        {
            _logger.LogInformation(
                "Purged {PurgedCount} queue metrics events older than {RetentionDays} days.",
                purged, retentionDays);
        }
    }
}
