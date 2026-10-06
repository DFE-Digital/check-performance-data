using DfE.CheckPerformanceData.Application.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="IAutomaticExerciseHandOver"/>
/// <remarks>
/// The clock is read the way <see cref="CheckingExerciseService"/> reads it
/// (<c>GetLocalNow</c>), because exercise dates are local wall-clock values. "Local" is the
/// server's zone, and the containers set none, so today that is UTC: during British Summer Time
/// "two hours after the end" is three hours after the UK end time (issue #535). That is one
/// defect with one fix — the clock — and must not be patched here alone.
///
/// The windows are read whole and filtered in memory. There are a handful of them, and it keeps
/// the due rule in one testable place instead of a second copy in SQL.
/// </remarks>
public sealed class AutomaticExerciseHandOver(
    IWindowRepository windows,
    ICloseExerciseService sweep,
    IWindowAdminAuditWriter audit,
    TimeProvider timeProvider,
    IOptions<ExerciseHandOverSettings> options,
    ILogger<AutomaticExerciseHandOver> logger) : IAutomaticExerciseHandOver
{
    public async Task<IReadOnlyList<DueExercise>> FindDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetLocalNow().DateTime;

        return (await windows.GetAllWindowsAsync(cancellationToken))
            .SelectMany(window => window.Exercises
                // A data share has no kind, and a change request reaches an exercise only through
                // its kind (WhatToChangeCheckingExerciseMap), so it never holds one to hand over.
                .Where(exercise => exercise.ExerciseType is not null
                    && ExerciseHandOverSchedule.IsDue(exercise.EndDate, now, settings))
                .Select(exercise => new DueExercise(
                    window.Id, window.Title, exercise.Id, exercise.ExerciseType!.Value, exercise.EndDate)))
            .ToList();
    }

    public async Task<AutomaticHandOverOutcome> HandOverAsync(DueExercise due, CancellationToken cancellationToken)
    {
        CloseExerciseResult result;
        try
        {
            result = await sweep.CloseAsync(due.WindowId, due.ExerciseId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sweep is not transactional (a blob read and a queue write per request), so it
            // may have sent some requests before it failed. That is safe: those rows are already
            // committed and the next run picks up only what is left. Ids only — no pupil data.
            logger.LogError(ex,
                "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}) of window {WindowId} failed. It is retried on the next run.",
                due.ExerciseId, due.Exercise, due.WindowId);
            return new AutomaticHandOverOutcome(due.WindowId, due.ExerciseId, due.Exercise, 0, 0, Failed: true);
        }

        var outcome = new AutomaticHandOverOutcome(
            due.WindowId, due.ExerciseId, due.Exercise, result.Enqueued, result.DraftsCancelled, Failed: false);

        // Nothing sent and nothing cancelled is the normal answer on every run after the first.
        // It is not worth an audit row, or the catch-up day would write hundreds.
        if (result.Enqueued == 0 && result.DraftsCancelled == 0)
        {
            return outcome;
        }

        logger.LogInformation(
            "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}) of window {WindowId}: {RequestsSent} request(s) sent for processing, {DraftsCancelled} draft(s) cancelled.",
            due.ExerciseId, due.Exercise, due.WindowId, result.Enqueued, result.DraftsCancelled);

        try
        {
            // Deliberately not the caller's token: the hand-over has already happened and cannot
            // be undone, so its record must not be abandoned because the host is stopping.
            await audit.RecordAutomaticHandOverAsync(new AutomaticHandOverAudit
            {
                WindowId = due.WindowId,
                WindowTitle = due.WindowTitle,
                ExerciseId = due.ExerciseId,
                Exercise = due.Exercise,
                ExerciseEnd = due.ExerciseEnd,
                RequestsSent = result.Enqueued,
                DraftsCancelled = result.DraftsCancelled,
                RanAtUtc = timeProvider.GetUtcNow().UtcDateTime
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The hand-over has happened and cannot be undone, so this is not reported as a
            // failed hand-over. The information line above is then the only record of it.
            logger.LogError(ex,
                "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}) of window {WindowId} happened, but its audit row could not be written.",
                due.ExerciseId, due.Exercise, due.WindowId);
        }

        return outcome;
    }
}
