using DfE.CheckPerformanceData.Application.ContentStaging;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;

/// <summary>
/// Bounds the growth of the content-staging session table.
///
/// A preview session holds the whole parsed bundle — up to the upload ceiling — so that the
/// confirm step does not have to round-trip it through the browser. Sessions expire, but expiry
/// is enforced when a session is READ; the only thing that deletes rows is an opportunistic
/// sweep inside the preview action itself. That makes deletion contingent on somebody starting
/// another import, and the rows that survive longest are precisely the ones nobody came back
/// for. In an environment where imports are occasional, a month of abandoned previews sits on
/// disk holding a month of CMS content.
///
/// This closes that off the way the rest of the codebase does it — a background sweep that runs
/// whether or not anyone is using the feature. Mirrors <see cref="MetricsRetentionJob"/>'s loop
/// and two-overload shape so the inner purge can be driven directly in a test.
///
/// The retention window is not configurable here on purpose: it belongs to the session's own
/// lifetime (<see cref="ContentStagingSessionDefaults.Lifetime"/>), which the read path already
/// enforces. An operator-tunable window would only let the two disagree.
/// </summary>
public sealed class ContentStagingSessionRetentionJob : BackgroundService
{
    // Sessions are few and small in number even when large in bytes, and their lifetime is
    // measured in hours, so there is nothing to gain from sweeping more often than this.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentStagingSessionRetentionJob> _logger;
    private readonly TimeSpan? _intervalOverride;

    public ContentStagingSessionRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ContentStagingSessionRetentionJob> logger,
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
                _logger.LogError(ex, "Content staging session retention tick failed.");
            }

            try
            {
                await Task.Delay(_intervalOverride ?? DefaultInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Public IServiceProvider entry point — used by ExecuteAsync each tick.
    public Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RunOnceAsync(services.GetRequiredService<IContentStagingSessionStore>(), cancellationToken);

    // Direct-injection overload — the unit tests call this shape.
    public async Task RunOnceAsync(IContentStagingSessionStore sessions, CancellationToken cancellationToken)
    {
        var purged = await sessions.PurgeExpiredAsync(cancellationToken);
        if (purged > 0)
        {
            _logger.LogInformation(
                "Purged {PurgedCount} expired content-staging session(s).", purged);
        }
    }
}
