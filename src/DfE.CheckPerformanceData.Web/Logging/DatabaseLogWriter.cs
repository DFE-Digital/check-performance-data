using DfE.CheckPerformanceData.Application.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Logging;

// Background service that drains the channel and batch-inserts into AppLogs. Runs for the
// lifetime of the application. On shutdown it drains anything already queued before returning
// so no in-flight logs are lost.
public sealed class DatabaseLogWriter(
    AppLogChannel channel,
    AppLogSinkOptions options,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<AppLogDto>(options.BatchSize);
        var flushInterval = options.FlushInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var window = new CancellationTokenSource(flushInterval);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, window.Token);

                try
                {
                    // Block until at least one entry is available, or the flush window expires.
                    var head = await channel.Channel.Reader.ReadAsync(linked.Token);
                    buffer.Add(head);
                }
                catch (OperationCanceledException) when (window.IsCancellationRequested)
                {
                    // Flush window expired with nothing queued — go round again.
                    continue;
                }

                // Drain everything currently available (up to the batch size) without waiting.
                while (buffer.Count < options.BatchSize
                       && channel.Channel.Reader.TryRead(out var next))
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
                // Never let a sink failure kill the writer — swallow, wait a bit, keep going.
                // We can't log this through the ILogger pipeline (that would recurse), so hit the
                // fallback Console. Once every few seconds max: don't storm on repeat failures.
                Console.Error.WriteLine($"[DatabaseLogWriter] flush failed: {ex.GetType().Name} — {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
                buffer.Clear();
            }
        }

        // Shutdown: drain remaining items so nothing in-flight is lost.
        while (channel.Channel.Reader.TryRead(out var tail))
            buffer.Add(tail);
        if (buffer.Count > 0)
        {
            try { await FlushAsync(buffer, CancellationToken.None); }
            catch { /* best-effort on shutdown */ }
        }
    }

    private async Task FlushAsync(List<AppLogDto> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        // Scoped: the repository (and its DbContext) is scoped by default, but the writer runs
        // as a singleton — resolve per flush.
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppLogRepository>();
        await repo.BulkInsertAsync(rows, cancellationToken);
    }
}
