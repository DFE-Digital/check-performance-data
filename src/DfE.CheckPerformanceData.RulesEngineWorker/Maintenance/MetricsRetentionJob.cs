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
    // Used only when the settings-driven interval cannot be read (e.g. before the settings store
    // is reachable). The live cadence comes from SettingKeys.MetricsRetentionIntervalMinutes,
    // re-read each tick so an operator change takes effect without a redeploy.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsRetentionJob> _logger;
    private readonly TimeSpan? _intervalOverride;

    public MetricsRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<MetricsRetentionJob> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _intervalOverride = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = _intervalOverride ?? DefaultInterval;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);

                // Re-read the cadence each tick (unless a fixed test override is supplied) so the
                // admin-editable MetricsRetentionIntervalMinutes setting actually drives the loop.
                if (_intervalOverride is null)
                {
                    interval = await ResolveIntervalAsync(scope.ServiceProvider);
                }
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
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<TimeSpan> ResolveIntervalAsync(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingService>();
        var minutes = await settings.GetIntAsync(SettingKeys.MetricsRetentionIntervalMinutes);
        return minutes > 0 ? TimeSpan.FromMinutes(minutes) : DefaultInterval;
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
