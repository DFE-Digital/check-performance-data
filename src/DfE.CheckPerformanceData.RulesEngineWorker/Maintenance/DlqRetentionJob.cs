using DfE.CheckPerformanceData.Application.Notifications;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;

/// <summary>
/// Periodically bounds dead-letter growth and surfaces queue health. On each tick it
/// purges dead-letter rows older than the settings-driven retention window (the admin
/// action audit trail is kept independently of the messages it describes), emits
/// structured signals for queue depth, dead-letter depth and oldest-message age, and
/// raises a GOV.UK Notify email to the configured recipients when the dead-letter depth
/// crosses the configured threshold. The alert carries the depth metadata only.
/// </summary>
public sealed class DlqRetentionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DlqRetentionJob> _logger;
    private readonly TimeSpan _interval;

    public DlqRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<DlqRetentionJob> logger,
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
                _logger.LogError(ex, "Dead-letter retention tick failed.");
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
            services.GetRequiredService<IQueueAdminService>(),
            services.GetRequiredService<INotifyClient>(),
            cancellationToken);

    public async Task RunOnceAsync(
        ISettingService settings,
        IQueueAdminService queueAdmin,
        INotifyClient notifyClient,
        CancellationToken cancellationToken)
    {
        var retentionDays = await settings.GetIntAsync(SettingKeys.DlqRetentionDays);
        if (retentionDays <= 0)
        {
            // A zero or negative retention computes a cutoff of "now" (or the future) and would
            // delete the entire dead-letter queue. Refuse it and skip the purge rather than
            // silently wipe the DLQ on the next tick; the depth signals and alert below still run.
            _logger.LogWarning(
                "Dead-letter retention is configured to {RetentionDays} days; skipping the purge to avoid wiping the dead-letter queue. Set Dlq:RetentionDays to a positive value.",
                retentionDays);
        }
        else
        {
            var purged = await queueAdmin.PurgeExpiredAsync(TimeSpan.FromDays(retentionDays), cancellationToken);
            if (purged > 0)
            {
                _logger.LogInformation(
                    "Purged {PurgedCount} dead-letter messages older than {RetentionDays} days.",
                    purged, retentionDays);
            }
        }

        var depths = await queueAdmin.GetQueueDepthsAsync(cancellationToken);
        foreach (var depth in depths)
        {
            _logger.LogInformation(
                "Queue depth signal: Queue={Queue} Depth={Depth} OldestMessageAgeSeconds={OldestAgeSeconds}",
                depth.QueueName, depth.Depth, depth.OldestMessageAge?.TotalSeconds);
        }

        var dlqDepth = await queueAdmin.GetDlqCountAsync(cancellationToken);
        _logger.LogInformation("Dead-letter depth signal: DlqDepth={DlqDepth}", dlqDepth);

        var threshold = await settings.GetIntAsync(SettingKeys.DlqAlertThreshold);
        if (dlqDepth <= threshold)
        {
            return;
        }

        var recipientsCsv = await settings.GetValueAsync(SettingKeys.DlqAlertRecipients);
        var recipients = recipientsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Dead-letter depth {DlqDepth} crossed the alert threshold {Threshold} but no alert recipients are configured.",
                dlqDepth, threshold);
            return;
        }

        _logger.LogWarning(
            "Dead-letter depth {DlqDepth} crossed the alert threshold {Threshold}; alerting {RecipientCount} recipient(s).",
            dlqDepth, threshold, recipients.Count);

        var personalisation = new Dictionary<string, object>
        {
            ["dlq_depth"] = dlqDepth,
            ["threshold"] = threshold
        };

        foreach (var recipient in recipients)
        {
            await notifyClient.SendEmailAsync(recipient, personalisation, cancellationToken);
        }
    }
}
