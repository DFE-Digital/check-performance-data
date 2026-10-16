using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.WindowManagement.Extensions;

/// <inheritdoc cref="IExerciseExtensionService"/>
/// <remarks>
/// The clock is read here only to STAMP a write. Whether an extension is open or closed is asked
/// of <see cref="ICheckingExerciseService"/>, the one place that compares dates with now, on the
/// same <see cref="TimeProvider"/> — as <see cref="ExerciseEarlyClosureService"/> does, and for
/// the same reason: the stamp and the comparison can never disagree.
///
/// The repository does not know which window an exercise is on, nor that a school is in at most
/// one extension of it; those checks are made here. Its unique index on the school is the
/// backstop, and this check is what gives the admin a sentence instead of an error page. When two
/// admins race past the check, the repository's <see cref="ExtensionSchoolTakenException"/> is
/// read again and turned into that same sentence.
/// </remarks>
public sealed class ExerciseExtensionService(
    IWindowRepository windows,
    IExerciseExtensionRepository extensions,
    ICheckingExerciseService checkingExercises,
    ICheckingDataReader checkingData,
    ICloseExerciseService sweep,
    TimeProvider timeProvider,
    ILogger<ExerciseExtensionService> logger) : IExerciseExtensionService
{
    public async Task<ExtensionWarnings> FindSchoolsWithoutDataAsync(
        Guid windowId, Guid exerciseId, IReadOnlyList<string> laestabs, CancellationToken ct)
    {
        var (exercise, _) = await FindExerciseAsync(windowId, exerciseId, ct);
        if (exercise is null) return new ExtensionWarnings([], ExerciseHasNoRelease: false);

        var digits = Digits(laestabs);
        // The warning is about the current release. With none yet there is nothing to look in, so
        // every school is warned of and storage is not asked about each.
        if (exercise.CurrentReleaseId is null) return new ExtensionWarnings(digits, ExerciseHasNoRelease: true);

        // One listing of the release's per-school files answers for every school, instead of a storage call per school.
        var withData = await checkingData.ListSchoolsWithDataAsync(windowId, exercise, ct);
        var without = digits.Where(laestab => !withData.Contains(laestab)).ToList();

        return new ExtensionWarnings(without, ExerciseHasNoRelease: false);
    }

    public async Task<ExtensionSaveResult> AddAsync(ExtensionCommand command, CancellationToken ct)
    {
        var (exercise, kind) = await FindExerciseAsync(command.WindowId, command.ExerciseId, ct);
        if (exercise is null) return new ExtensionSaveResult(ExtensionSaveStatus.NotFound);

        var digits = Digits(command.Laestabs);
        if (Refusal(exercise, extensionId: null, command.EndDate, digits) is { } refused) return refused;

        Guid id;
        try
        {
            id = await extensions.AddAsync(new NewExtension
            {
                WindowId = command.WindowId,
                ExerciseId = command.ExerciseId,
                Exercise = kind,
                EndDate = command.EndDate,
                Laestabs = digits,
                Actor = command.Actor,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            }, ct);
        }
        catch (ExtensionSchoolTakenException)
        {
            return await LostRaceAsync(command, extensionId: null, digits, ct);
        }

        return new ExtensionSaveResult(ExtensionSaveStatus.Saved, id);
    }

    public async Task<ExtensionSaveResult> EditAsync(
        Guid extensionId, int expectedVersion, ExtensionCommand command, CancellationToken ct)
    {
        var (exercise, kind) = await FindExerciseAsync(command.WindowId, command.ExerciseId, ct);
        var extension = exercise?.Extensions.SingleOrDefault(x => x.Id == extensionId);
        if (exercise is null || extension is null) return new ExtensionSaveResult(ExtensionSaveStatus.NotFound);

        // A closed extension (its own end has passed) is read only: its requests may already have
        // been sent. Its schools stay named in it, and the conflict check in Refusal counts closed
        // extensions too, so as built a school whose extension has closed cannot be given more time
        // on this exercise by another extension either.
        if (checkingExercises.HasClosed(extension)) return new ExtensionSaveResult(ExtensionSaveStatus.Closed);

        var digits = Digits(command.Laestabs);
        if (Refusal(exercise, extensionId, command.EndDate, digits) is { } refused) return refused;

        bool written;
        try
        {
            written = await extensions.UpdateAsync(new ExtensionEdit
            {
                WindowId = command.WindowId,
                ExerciseId = command.ExerciseId,
                Exercise = kind,
                ExtensionId = extensionId,
                ExpectedVersion = expectedVersion,
                PreviousEndDate = extension.EndDate,
                NewEndDate = command.EndDate,
                Laestabs = digits,
                Actor = command.Actor,
                EditedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            }, ct);
        }
        catch (ExtensionSchoolTakenException)
        {
            return await LostRaceAsync(command, extensionId, digits, ct);
        }

        // False means the version moved after the admin read it: another edit, a second press, or
        // an early close. This edit would overwrite what they did not see, so nothing is written.
        if (!written) return new ExtensionSaveResult(ExtensionSaveStatus.Stale);

        // A school taken out after the exercise's own end has no extra time left: the exercise's
        // sweep already ran, and after its catch-up day nothing automatic runs again. So the edit sweeps the removed schools itself. While the exercise still runs,
        // the exercise's own sweep takes them when it ends.
        var removed = extension.Laestabs.Except(digits).ToList();
        if (removed.Count == 0 || !checkingExercises.HasClosed(exercise))
            return new ExtensionSaveResult(ExtensionSaveStatus.Saved, extensionId, RemovedSchools: removed);

        try
        {
            var swept = await sweep.SweepSchoolsAsync(command.WindowId, command.ExerciseId, removed, ct);
            return new ExtensionSaveResult(ExtensionSaveStatus.Saved, extensionId, RemovedSchools: removed, RemovedSchoolsSwept: swept);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The edit is committed; the sweep is repeatable from the exercise's Send page. Ids only.
            logger.LogError(ex,
                "Sweep of {Count} school(s) removed from extension {ExtensionId} of exercise {ExerciseId} (window {WindowId}) failed.",
                removed.Count, extensionId, command.ExerciseId, command.WindowId);
            return new ExtensionSaveResult(ExtensionSaveStatus.Saved, extensionId, RemovedSchools: removed, RemovedSchoolsSweepFailed: true);
        }
    }

    public async Task<EarlyClosureResult> CloseEarlyAsync(
        Guid windowId, Guid exerciseId, Guid extensionId, ExtensionActor actor, CancellationToken ct)
    {
        var (exercise, kind) = await FindExerciseAsync(windowId, exerciseId, ct);
        var extension = exercise?.Extensions.SingleOrDefault(x => x.Id == extensionId);
        if (extension is null) return EarlyClosureResult.NotFound;

        if (!checkingExercises.IsOpen(extension)) return EarlyClosureResult.NotOpen;

        DateTime closedAt = ToTheSecond(timeProvider.GetLocalNow().DateTime);

        bool written = await extensions.CloseEarlyAsync(new ExtensionEarlyClosure
        {
            WindowId = windowId,
            ExerciseId = exerciseId,
            Exercise = kind,
            ExtensionId = extensionId,
            ScheduledEnd = extension.EndDate,
            // One second before the close, not the close itself: an extension's last instant is
            // open (EndDate >= now), so ending it AT now would leave it open for that instant.
            NewEndDate = closedAt.AddSeconds(-1),
            Actor = actor,
            ClosedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        }, ct);

        // False means the end date changed between the read above and the write: someone else
        // closed or edited it. Either way this press closed nothing.
        return written ? EarlyClosureResult.Closed(closedAt, extension.EndDate) : EarlyClosureResult.NotOpen;
    }

    // The exercise only when the window holds it and it has a kind: a data share has no journey,
    // so there is no time to extend.
    private async Task<(CheckingExerciseDto? Exercise, CheckingExerciseType Kind)> FindExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken ct)
    {
        var window = await windows.GetByIdAsync(windowId, ct);
        var row = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        return row?.ExerciseType is { } kind ? (row, kind) : (null, default);
    }

    // The add and edit checks after the extension's own, in order; null when the write may go ahead.
    private ExtensionSaveResult? Refusal(
        CheckingExerciseDto exercise, Guid? extensionId, DateTime endDate, IReadOnlyList<string> laestabs)
    {
        // A school's end is never earlier than the exercise's own, so an end on or before it would
        // save an extension that adds no time.
        if (endDate <= exercise.EndDate)
        {
            return new ExtensionSaveResult(ExtensionSaveStatus.EndDateNotAfterExercise, ExerciseEnd: exercise.EndDate);
        }

        // An end already past would save an extension that is closed the moment it is written: no
        // school gains time, and its schools are then locked out of any later extension. Asked of
        // the one clock — an extension with that end is open exactly when the end is still ahead.
        if (!checkingExercises.IsOpen(new CheckingExerciseExtensionDto { EndDate = endDate }))
        {
            return new ExtensionSaveResult(ExtensionSaveStatus.EndDateNotInFuture);
        }

        if (laestabs.Count == 0) return new ExtensionSaveResult(ExtensionSaveStatus.NoSchools);

        // Any other extension of the exercise, open or closed: a school is in at most one, and on
        // an edit the extension's own schools are not a conflict with itself.
        var conflicts = laestabs
            .Select(laestab => (laestab, other: exercise.Extensions
                .FirstOrDefault(x => x.Id != extensionId && x.Contains(laestab))))
            .Where(c => c.other is not null)
            .Select(c => new ExtensionConflict(c.laestab, c.other!.EndDate))
            .ToList();

        return conflicts.Count > 0
            ? new ExtensionSaveResult(ExtensionSaveStatus.SchoolInAnotherExtension, Conflicts: conflicts)
            : null;
    }

    // The unique index refused the write: another admin's add or edit took one of these schools
    // between this call's read and its write. Read again so the admin is told which school and which
    // extension, exactly as the check before the write would have said. If nothing conflicts any
    // more (the competing extension has gone again), nothing the admin typed is wrong and nothing was
    // written, so they go back to the summary as an edit that lost a race does.
    private async Task<ExtensionSaveResult> LostRaceAsync(
        ExtensionCommand command, Guid? extensionId, IReadOnlyList<string> digits, CancellationToken ct)
    {
        var (exercise, _) = await FindExerciseAsync(command.WindowId, command.ExerciseId, ct);
        if (exercise is null) return new ExtensionSaveResult(ExtensionSaveStatus.NotFound);

        return Refusal(exercise, extensionId, command.EndDate, digits)
               ?? new ExtensionSaveResult(ExtensionSaveStatus.Stale);
    }

    // Digits only, once each, in the order given: the form the repository stores and every blob
    // name uses. The form hands over LaestabList's output already, but a caller may not.
    private static IReadOnlyList<string> Digits(IEnumerable<string> laestabs) =>
        laestabs.Select(LaestabNormaliser.Normalise).Where(d => d.Length > 0).Distinct().ToList();

    // Exercise dates are shown and edited to the minute; sub-second precision would only make the
    // stored value differ from anything a person could read back.
    private static DateTime ToTheSecond(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified);
}
