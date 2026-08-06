using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;

/// <summary>
/// Bounds the growth of the search-analytics tables. Runs each tick, resolves the current
/// retention setting from the settings store, and drives BOTH the events sink AND the
/// messages service to purge expired rows in their respective tables. Two purge dependencies
/// on purpose — combining both delete paths behind one interface would give the retention
/// job two ways to reach the same table, and the two owners would drift out of sync;
/// keeping events on ISearchAnalyticsSink and messages on ISearchMessageService means one
/// owner per table set.
///
/// Mirrors MetricsRetentionJob's two-overload shape (public IServiceProvider entry point
/// forwards to a public direct-injection overload) so the inner purge logic can be unit-
/// tested without spinning a DI container.
///
/// Retention values are clamped in code so a mis-configured admin setting cannot disable
/// the ceiling — events at 1..365 days, messages at 1..730 days.
/// </summary>
public sealed class SearchAnalyticsRetentionJob : BackgroundService
{
    // Fallback interval used only before the settings store is reachable; the live
    // cadence comes from SettingKeys.SearchAnalyticsRetentionIntervalMinutes.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchAnalyticsRetentionJob> _logger;
    private readonly TimeSpan? _intervalOverride;

    public SearchAnalyticsRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SearchAnalyticsRetentionJob> logger,
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
                _logger.LogError(ex, "Search analytics retention tick failed.");
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
        var minutes = await settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionIntervalMinutes);
        return minutes > 0 ? TimeSpan.FromMinutes(minutes) : DefaultInterval;
    }

    // Public IServiceProvider entry point — used by ExecuteAsync each tick.
    public Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RunOnceAsync(
            services.GetRequiredService<ISettingService>(),
            services.GetRequiredService<ISearchAnalyticsSink>(),
            services.GetRequiredService<ISearchMessageService>(),
            cancellationToken);

    // Direct-injection overload — the unit tests call this shape.
    public async Task RunOnceAsync(
        ISettingService settings,
        ISearchAnalyticsSink sink,
        ISearchMessageService messageService,
        CancellationToken cancellationToken)
    {
        // Bounds are shared with the dashboard's prior-window availability gate so the two
        // cannot drift — see SearchAnalyticsRetention.
        var eventsDays = SearchAnalyticsRetention.ClampEventDays(
            await settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays));
        var eventsPurged = await sink.PurgeExpiredAsync(
            TimeSpan.FromDays(eventsDays), cancellationToken);
        if (eventsPurged > 0)
        {
            _logger.LogInformation(
                "Purged {PurgedCount} search analytics events older than {RetentionDays} days.",
                eventsPurged, eventsDays);
        }

        var messagesDays = SearchAnalyticsRetention.ClampMessageDays(
            await settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays));
        var messagesPurged = await messageService.PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(messagesDays), cancellationToken);
        if (messagesPurged > 0)
        {
            _logger.LogInformation(
                "Purged {PurgedCount} search messages older than {RetentionDays} days.",
                messagesPurged, messagesDays);
        }
    }
}
