using DfE.CheckPerformanceData.Application.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Web.Analytics;

// Background service that drains SearchAnalyticsChannel and batch-inserts through
// ISearchAnalyticsSink. Runs for the lifetime of the application. On shutdown it drains
// anything already queued before returning so no in-flight events are lost.
//
// Design mirrors DatabaseLogWriter (the AppLog sink counterpart) so the two channels
// share the same "await first item, drain to batch cap, flush, swallow sink failures,
// keep running" shape. Two differences from DatabaseLogWriter:
//
//   1. Batch cap + flush interval are constants on the type (100 rows / 2 seconds) since
//      the search sink has no matching AppLogSinkOptions binding. The constructor accepts
//      optional overrides so integration tests can compress them without touching prod.
//   2. Sink failures log through the ILogger pipeline (DatabaseLogWriter can't because it
//      is upstream of ILogger). Whatever comes through here is diagnostic — the burst
//      that failed will drain on the next tick.
public sealed class SearchEventWriter : BackgroundService
{
    // Defaults locked to the plan's spec (§7 / D-05a). Constructor overrides exist so
    // integration tests can compress cadence without a per-run config binding.
    public const int DefaultBatchSize = 100;
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    private readonly SearchAnalyticsChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchEventWriter> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _retryDelay;

    public SearchEventWriter(
        SearchAnalyticsChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<SearchEventWriter> logger,
        int? batchSize = null,
        TimeSpan? flushInterval = null,
        TimeSpan? retryDelay = null)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchSize = batchSize ?? DefaultBatchSize;
        _flushInterval = flushInterval ?? DefaultFlushInterval;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<SearchEventDto>(_batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var window = new CancellationTokenSource(_flushInterval);
                using var linked =
                    CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, window.Token);

                try
                {
                    var head = await _channel.Channel.Reader.ReadAsync(linked.Token);
                    buffer.Add(head);
                }
                catch (OperationCanceledException) when (window.IsCancellationRequested)
                {
                    continue;
                }

                while (buffer.Count < _batchSize
                       && _channel.Channel.Reader.TryRead(out var next))
                {
                    buffer.Add(next);
                }

                await FlushAsync(buffer, stoppingToken);
                buffer.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a sink failure kill the writer. Log the batch loss, back off,
                // clear the buffer, and pick up on the next tick. Rows in the failed
                // batch are dropped — the acceptable trade-off for a fire-and-forget
                // analytics pipeline that must not throttle the /search request path.
                _logger.LogError(ex,
                    "Search analytics batch write failed; {Count} events dropped.",
                    buffer.Count);
                try { await Task.Delay(_retryDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
                buffer.Clear();
            }
        }

        // Shutdown drain — flush anything queued so no in-flight event is lost.
        while (_channel.Channel.Reader.TryRead(out var tail))
        {
            buffer.Add(tail);
        }
        if (buffer.Count > 0)
        {
            try { await FlushAsync(buffer, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Search analytics shutdown flush failed; {Count} events dropped.",
                    buffer.Count);
            }
        }
    }

    private async Task FlushAsync(List<SearchEventDto> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        // The sink is scoped (owns an IPortalDbContext); the writer is a singleton
        // hosted service — resolve per flush so the DbContext lifetime matches
        // production's Scoped registration.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<ISearchAnalyticsSink>();
        await sink.RecordBatchAsync(batch, cancellationToken);
    }
}
