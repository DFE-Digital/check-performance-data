using DfE.CheckPerformanceData.Application.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <inheritdoc cref="IAutomaticExerciseHandOver"/>
/// <remarks>
/// The clock is read the way <see cref="CheckingExerciseService"/> reads it
/// (<c>GetLocalNow</c>), because exercise dates are UK wall-clock values. "Local" is UK time in
/// every environment: the registered <see cref="TimeProvider"/> is
/// <see cref="DfE.CheckPerformanceData.Application.Common.UkTimeProvider"/> (#535), so "two
/// hours after the end" is two hours after the UK end time, summer and winter.
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
                .Where(exercise => exercise.ExerciseType is not null)
                .SelectMany(exercise => DueItems(window, exercise, now, settings)))
            .ToList();
    }

    // The exercise first, then its extensions in list order. An extension is judged on its
    // effective end, max(exercise end, extension end) (#567): it usually runs past the exercise's
    // end and falls due later and separately, but one closed early (or overtaken by a later
    // exercise end) must wait for the exercise, or its sweep would cancel the drafts of schools
    // still inside the exercise's own dates.
    private static IEnumerable<DueExercise> DueItems(
        CheckingWindowDto window, CheckingExerciseDto exercise, DateTime now, ExerciseHandOverSettings settings)
    {
        if (ExerciseHandOverSchedule.IsDue(exercise.EndDate, now, settings))
        {
            yield return new DueExercise(
                window.Id, window.Title, exercise.Id, exercise.ExerciseType!.Value, exercise.EndDate);
        }

        foreach (var extension in exercise.Extensions)
        {
            var end = exercise.EffectiveEndDateOf(extension);
            if (ExerciseHandOverSchedule.IsDue(end, now, settings))
            {
                yield return new DueExercise(
                    window.Id, window.Title, exercise.Id, exercise.ExerciseType!.Value, end, extension.Id);
            }
        }
    }

    public async Task<AutomaticHandOverOutcome> HandOverAsync(DueExercise due, CancellationToken cancellationToken)
    {
        CloseExerciseResult result;
        try
        {
            result = due.ExtensionId is null
                ? await sweep.CloseAsync(due.WindowId, due.ExerciseId, cancellationToken)
                : await sweep.CloseExtensionAsync(due.WindowId, due.ExerciseId, due.ExtensionId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sweep is not transactional (a blob read and a queue write per request), so it
            // may have sent some requests before it failed. That is safe: those rows are already
            // committed and the next run picks up only what is left. Ids only — no pupil data.
            logger.LogError(ex,
                "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}){Extension} of window {WindowId} failed. It is retried on the next run.",
                due.ExerciseId, due.Exercise, ExtensionText(due), due.WindowId);
            return new AutomaticHandOverOutcome(due.WindowId, due.ExerciseId, due.Exercise, 0, 0, Failed: true, due.ExtensionId);
        }

        var outcome = new AutomaticHandOverOutcome(
            due.WindowId, due.ExerciseId, due.Exercise, result.Enqueued, result.DraftsCancelled, Failed: false, due.ExtensionId);

        // Nothing sent and nothing cancelled is the normal answer on every run after the first.
        // It is not worth an audit row, or the catch-up day would write hundreds.
        if (result.Enqueued == 0 && result.DraftsCancelled == 0)
        {
            return outcome;
        }

        logger.LogInformation(
            "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}){Extension} of window {WindowId}: {RequestsSent} request(s) sent for processing, {DraftsCancelled} draft(s) cancelled.",
            due.ExerciseId, due.Exercise, ExtensionText(due), due.WindowId, result.Enqueued, result.DraftsCancelled);

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
                ExtensionId = due.ExtensionId,
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
                "Automatic hand-over of checking exercise {ExerciseId} ({Exercise}){Extension} of window {WindowId} happened, but its audit row could not be written.",
                due.ExerciseId, due.Exercise, ExtensionText(due), due.WindowId);
        }

        return outcome;
    }

    // Ids only, like the rest of the log lines; empty for the exercise's own hand-over.
    private static string ExtensionText(DueExercise due) =>
        due.ExtensionId is null ? "" : $" extension {due.ExtensionId}";
}
