using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Web.Maintenance;

/// <summary>
/// AB#302158: every few minutes, hands over the requests of each checking exercise that ended at
/// least two hours ago (<see cref="IAutomaticExerciseHandOver"/>).
/// </summary>
/// <remarks>
/// It lives in the web host, not the worker, because the hand-over rebuilds each request from the
/// question-flow files and the journey blobs, and only the web image has those.
///
/// Every web pod runs this, so each tick first takes <see cref="IExerciseHandOverLock"/>; a pod
/// that does not get it skips the tick. Each exercise is then handed over in a dependency scope
/// of its own — and so on a DbContext of its own — so a save that fails part-way through one
/// exercise's sweep cannot leave tracked entities behind for the next.
///
/// Mirrors the worker's retention jobs: a loop that survives a failed tick, and a public
/// <see cref="RunOnceAsync"/> the unit tests drive directly.
/// </remarks>
public sealed class ExerciseHandOverJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ExerciseHandOverSettings> options,
    ILogger<ExerciseHandOverJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ExerciseHandOverSettings settings;
        try
        {
            settings = options.Value;
        }
        catch (Exception ex)
        {
            // An exception escaping ExecuteAsync stops the whole web host, so one mistyped
            // setting would take the site down. Fail safe, and loudly: the job is off, the site up.
            logger.LogError(ex,
                "The ExerciseHandOver settings could not be read, so automatic exercise hand-over is not running. Correct the configuration and restart.");
            return;
        }

        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Automatic exercise hand-over is switched off (ExerciseHandOver:Enabled is false).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad tick end the loop: the next one may well succeed.
                logger.LogError(ex, "Automatic exercise hand-over tick failed.");
            }

            try
            {
                await Task.Delay(settings.EffectivePollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One tick: take the lock, find what is due, hand each over, release the lock.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handOverLock = scope.ServiceProvider.GetRequiredService<IExerciseHandOverLock>();

        if (!await handOverLock.TryAcquireAsync(cancellationToken))
        {
            logger.LogDebug("Another instance holds the exercise hand-over lock; skipping this tick.");
            return;
        }

        try
        {
            var due = await scope.ServiceProvider
                .GetRequiredService<IAutomaticExerciseHandOver>()
                .FindDueAsync(cancellationToken);

            foreach (var exercise in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var exerciseScope = scopeFactory.CreateAsyncScope();
                // Not the tick's token: a hand-over is a run of queue writes and row updates with
                // no transaction around it, so stopping it part-way leaves requests queued whose
                // rows still read unsent. It is quicker and safer to let the exercise in hand
                // finish and start no other. If the sweep outlasts the host's shutdown timeout
                // the process is killed anyway, which is no worse than before.
                await exerciseScope.ServiceProvider
                    .GetRequiredService<IAutomaticExerciseHandOver>()
                    .HandOverAsync(exercise, CancellationToken.None);
            }
        }
        finally
        {
            // Not the tick's token: a shutdown mid-tick must still hand the lock back, or the
            // next pod waits for this one's connection to die.
            await handOverLock.ReleaseAsync(CancellationToken.None);
        }
    }
}
