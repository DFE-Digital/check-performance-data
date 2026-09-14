using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The Preprocessing step (AB#294553). Server-side, deterministic, reported one step at a time.
/// All-or-nothing: a single failing record leaves the run PreprocessingFailed with every failure
/// listed and NOTHING written, so no record is ever silently lost. Cancellation (the browser left
/// the stream) puts the run back where it was; the only durable write is the final step.
/// </summary>
public sealed class EgressPreprocessor(IEgressRunRepository repository, IWindowService windows, TimeProvider clock, ILogger<EgressPreprocessor> logger) : IEgressPreprocessor
{
    public static readonly string[] StepNames =
    [
        "Filter records",
        EgressRecordBuilder.StepCodes,
        EgressRecordBuilder.StepSplit,
        EgressRecordBuilder.StepDates,
        EgressRecordBuilder.StepBuild,
        EgressRecordBuilder.StepTrim,
        LdsSpecValidator.StepName,
        "Save to database"
    ];

    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public async IAsyncEnumerable<EgressProgress> RunAsync(Guid runId, [EnumeratorCancellation] CancellationToken ct)
    {
        var run = await repository.GetRunAsync(runId, ct);
        if (run is null)
        {
            yield return Terminal(0, "Preprocessing", "This egress run no longer exists.", 0, 0, 0, isError: true, null);
            yield break;
        }
        // M2: a PreprocessingFailed run releases its pair (IsActive=false) so that a fresh run for
        // the same window/type is admitted — the Failed page's own copy says "start a new run".
        // Re-running the failed run instead could reach Preprocessed while a colleague's fresh run
        // also holds the pair, and both could then transfer it. A failed run is terminal here; it
        // gets the same refusal shape as any other non-runnable status.
        if (run.Status is not (EgressRunStatus.Pulled or EgressRunStatus.Preprocessed))
        {
            // Second-pass nit: use the same human label Index.cshtml shows for this status, not
            // the raw enum member name (e.g. "PreprocessingFailed" leaking straight to the user).
            yield return Terminal(0, "Preprocessing", $"This run is {EgressRunStatuses.Label(run.Status)} and cannot be preprocessed.", 0, 0, 0, isError: true, null);
            yield break;
        }
        var previous = run.Status;
        if (!await repository.TrySetStatusAsync(runId, previous, EgressRunStatus.Preprocessing, ct))
        {
            yield return Terminal(0, "Preprocessing", "This run is being preprocessed by someone else.", 0, 0, 0, isError: true, null);
            yield break;
        }

        var completed = false;
        try
        {
            await foreach (var progress in StepsAsync(run, ct))
            {
                if (progress.IsComplete) completed = true;
                yield return progress;
            }
        }
        finally
        {
            if (!completed)
            {
                // Aborted mid-run (the stream was closed, or an exception): nothing durable happened
                // after the status flip, so undo the flip. CancellationToken.None — the token that
                // got us here is already cancelled.
                await repository.TrySetStatusAsync(runId, EgressRunStatus.Preprocessing, previous, CancellationToken.None);
            }
        }
    }

    private async IAsyncEnumerable<EgressProgress> StepsAsync(EgressRunDto run, [EnumeratorCancellation] CancellationToken ct)
    {
        var all = run.Outputs.SelectMany(o => o.Records).ToList();

        // 1. Filter
        yield return Running(1, all.Count);
        ct.ThrowIfCancellationRequested();
        var items = all.Where(r => EgressDecisions.IsApproved(r.Decision)).Select(r => new EgressWorkItem(r)).ToList();
        yield return Done(1, all.Count, items.Count, 0, $"{items.Count} of {all.Count} records are approved or auto-approved; {all.Count - items.Count} discarded.");

        // 2-6. Per-record transforms
        Action<EgressWorkItem>[] transforms = [EgressRecordBuilder.DeriveCodes, EgressRecordBuilder.SplitEstablishment, EgressRecordBuilder.StandardiseDates, EgressRecordBuilder.Build, EgressRecordBuilder.Trim];
        for (var i = 0; i < transforms.Length; i++)
        {
            var step = i + 2;
            yield return Running(step, items.Count);
            ct.ThrowIfCancellationRequested();
            foreach (var item in items) transforms[i](item);
            var failed = items.Count(x => x.HasFailed);
            yield return Done(step, items.Count, items.Count - failed, failed, failed == 0 ? "Completed." : $"{failed} record(s) have problems so far.");
        }

        // 7. Validate
        yield return Running(7, items.Count);
        ct.ThrowIfCancellationRequested();
        foreach (var item in items.Where(x => !x.HasFailed))
        {
            var failures = item.NewRow is { } n ? LdsSpecValidator.Validate(n)
                : item.RemoveRow is { } r ? LdsSpecValidator.Validate(r) : [];
            item.Failures.AddRange(failures);
        }
        var allFailures = items.SelectMany(x => x.Failures).ToList();
        yield return Done(7, items.Count, items.Count(x => !x.HasFailed), allFailures.Count,
            allFailures.Count == 0 ? "Every record conforms to the LDS spec." : $"{allFailures.Count} problem(s) across {items.Count(x => x.HasFailed)} record(s).");

        // 8. Save (or fail the batch)
        yield return Running(8, items.Count);
        ct.ThrowIfCancellationRequested();
        if (allFailures.Count > 0)
        {
            // M4: 0 rows means the run was abandoned by someone else while this pipeline ran — do
            // not report PreprocessingFailed for a run that is actually Abandoned.
            if (await repository.MarkPreprocessingFailedAsync(run.Id, EgressRunStatus.Preprocessing, allFailures, ct) == 0)
            {
                logger.LogWarning("Egress run {RunId} was abandoned while preprocessing; its failures were not recorded", run.Id);
                yield return Terminal(8, StepNames[7], "This run was abandoned while preprocessing.", items.Count, 0, allFailures.Count, isError: true, null);
                yield break;
            }
            logger.LogWarning("Egress run {RunId} failed preprocessing with {Count} record failure(s)", run.Id, allFailures.Count);
            yield return Terminal(8, StepNames[7], "Preprocessing stopped: no records were saved because some records failed. Correct the source data and start a new run.",
                items.Count, 0, allFailures.Count, isError: true, EgressRunStatus.PreprocessingFailed);
            yield break;
        }

        // Derived from the window itself, not guessed from records — a run with no pulled records
        // (a window with no candidate requests at all) still needs the right stage in its file name.
        var window = await windows.GetByIdAsync(run.WindowId, ct)
            ?? throw new InvalidOperationException($"Checking window {run.WindowId} for egress run {run.Id} no longer exists.");
        var windowType = window.CheckingWindowType;
        var exportDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), London).DateTime);
        var fileNames = run.Outputs.ToDictionary(o => o.OutputType, o => EgressOutputTypes.FileName(windowType, o.OutputType, exportDate));
        var newRows = items.Select(x => x.NewRow).OfType<NewLearnerRow>().ToList();
        var removeRows = items.Select(x => x.RemoveRow).OfType<RemoveLearnerRow>().ToList();
        // M4: 0 rows means the run was abandoned by someone else while this pipeline ran — do not
        // report Preprocessed (with nothing actually saved) for a run that is actually Abandoned.
        if (await repository.SavePreprocessedAsync(run.Id, EgressRunStatus.Preprocessing, newRows, removeRows, exportDate, fileNames, ct) == 0)
        {
            logger.LogWarning("Egress run {RunId} was abandoned while preprocessing; nothing was saved", run.Id);
            yield return Terminal(8, StepNames[7], "This run was abandoned while preprocessing.", items.Count, 0, 0, isError: true, null);
            yield break;
        }
        yield return Terminal(8, StepNames[7], $"Saved {newRows.Count + removeRows.Count} record(s) to the database.", items.Count, newRows.Count + removeRows.Count, 0,
            isError: false, EgressRunStatus.Preprocessed);
    }

    private static EgressProgress Running(int step, int recordsIn) =>
        new(step, StepNames.Length, StepNames[step - 1], "running", recordsIn, 0, 0, false, false, $"{StepNames[step - 1]}…", null);

    private static EgressProgress Done(int step, int recordsIn, int recordsOut, int failures, string message) =>
        new(step, StepNames.Length, StepNames[step - 1], "done", recordsIn, recordsOut, failures, false, false, message, null);

    private static EgressProgress Terminal(int step, string stepName, string message, int recordsIn, int recordsOut, int failures, bool isError, EgressRunStatus? final) =>
        new(step, StepNames.Length, stepName, isError ? "failed" : "done", recordsIn, recordsOut, failures, true, isError, message, final);
}
