using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.Egress;

/// <summary>
/// Data egress to LDS (AB#294553): Pull → Results → Preprocessing → Summary → Complete. A run is
/// persisted at pull time, so every later page is a GET on the run id and "save" is just leaving;
/// Resume sends the user to whichever page the run's status implies. Preprocessing streams progress
/// over server-sent events with a plain POST fallback driving the same enumerable
/// (ValidateWindowController pattern).
/// </summary>
[RequireAdminSection(AdminNavKeys.Egress)]
[Route("admin/egress")]
public sealed class EgressController(
    IEgressRunService runs,
    IEgressPreprocessor preprocessor,
    IEgressTransferService transfer,
    IEgressBlobClient blobs,
    IWindowService windows,
    ICurrentUserService currentUser) : Controller
{
    public const string BannerKey = "EgressBanner";
    public const string TransferErrorKey = "EgressTransferError";

    private static readonly EgressRunStatus[] SavedStatuses =
        [EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, EgressRunStatus.PreprocessingFailed, EgressRunStatus.Preprocessed, EgressRunStatus.Transferring, EgressRunStatus.TransferFailed];

    private EgressActor Actor => new(Guid.TryParse(currentUser.UserId, out var id) ? id : Guid.Empty, currentUser.DisplayName, currentUser.Email);

    // ---------------------------------------------------------------- Pull

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View("Index", await PullModelAsync(new PullForm(), null, null, [], null, cancellationToken));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(PullForm form, CancellationToken cancellationToken)
    {
        var windowError = form.WindowId is null || form.WindowId == Guid.Empty ? "Select a checking window" : null;
        // S9: an unbindable OutputTypes value (e.g. OutputTypes=garbage) previously bound as
        // default(EgressOutputType) — NewLearners — with a ModelState error nobody read, so the
        // request silently proceeded as if NewLearners had been ticked. Checking the field's own
        // validation state catches that case without mislabelling an unrelated WindowId failure
        // (already handled above) as an output-types error.
        var typesError = form.OutputTypes.Count == 0
            || ModelState.GetValidationState(nameof(PullForm.OutputTypes)) == ModelValidationState.Invalid
                ? "Select at least one output type" : null;
        if (windowError is not null || typesError is not null)
            return View("Index", await PullModelAsync(form, windowError, typesError, [], null, cancellationToken));

        var result = await runs.StartAsync(form.WindowId!.Value, form.OutputTypes.Distinct().ToList(), Actor, cancellationToken);
        return result switch
        {
            EgressStartResult.Started started => RedirectToAction(nameof(Results), new { id = started.RunId }),
            EgressStartResult.Refused refused => View("Index", await PullModelAsync(form, null, null, refused.Blockers.Select(Describe).ToList(), null, cancellationToken)),
            EgressStartResult.WindowNotFound => View("Index", await PullModelAsync(form, "Select a checking window", null, [], null, cancellationToken)),
            EgressStartResult.PullFailed failed => View("Index", await PullModelAsync(form, null, null, [], failed.Reason, cancellationToken)),
            _ => throw new InvalidOperationException("Unknown start result")
        };
    }

    // FLAGGED copy: the two refusal sentences (AB#294553 "Concurrency").
    private static string Describe((EgressOutputType OutputType, EgressBlocker Blocker) refusal)
    {
        var (type, b) = refusal;
        var label = EgressOutputTypes.Label(type);
        return b.Status == EgressRunStatus.Transferred
            ? $"{label} for this checking window has already been transferred to LDS by {b.TransferredByName ?? b.StartedByName} on {b.TransferredAtUtc:d MMMM yyyy 'at' HH:mm} UTC. It cannot be sent again."
            : $"{label} for this checking window is already being processed by {b.StartedByName}, started {b.StartedAtUtc:d MMMM yyyy 'at' HH:mm} UTC. Wait for that run to finish or be abandoned.";
    }

    private async Task<PullViewModel> PullModelAsync(PullForm form, string? windowError, string? typesError, IReadOnlyList<string> refusals, string? pullError, CancellationToken ct)
    {
        var all = (await windows.GetAllDataAsync(ct))?.Windows ?? [];
        var list = await runs.ListAsync(ct);
        return new PullViewModel
        {
            Windows = all.OrderByDescending(w => w.StartDate).Select(w => new WindowChoice(w.Id, $"{w.Title} ({EgressOutputTypes.StageToken(w.CheckingWindowType)})")).ToList(),
            OutputTypes = EgressOutputTypes.All,
            SelectedWindowId = form.WindowId,
            SelectedOutputTypes = form.OutputTypes,
            WindowError = windowError,
            OutputTypesError = typesError,
            Refusals = refusals,
            PullError = pullError,
            SavedRuns = list.Where(r => SavedStatuses.Contains(r.Status)).ToList(),
            CompletedRuns = list.Where(r => r.Status == EgressRunStatus.Transferred).ToList(),
            Banner = TempData[BannerKey] as string
        };
    }

    // ---------------------------------------------------------------- Resume

    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(id, cancellationToken);
        if (run is null) return NotFound();
        return run.Status switch
        {
            EgressRunStatus.Pulled => RedirectToAction(nameof(Results), new { id }),
            EgressRunStatus.Preprocessing => RedirectToAction(nameof(Preprocessing), new { id }),
            EgressRunStatus.PreprocessingFailed => RedirectToAction(nameof(Failed), new { id }),
            EgressRunStatus.Preprocessed or EgressRunStatus.TransferFailed or EgressRunStatus.Transferring => RedirectToAction(nameof(Summary), new { id }),
            EgressRunStatus.Transferred => RedirectToAction(nameof(Complete), new { id }),
            _ => Home("That egress run was abandoned. Start a new one if it is still needed.")
        };
    }

    private IActionResult Home(string banner)
    {
        TempData[BannerKey] = banner;
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- Results

    [HttpGet("runs/{id:guid}/results")]
    public async Task<IActionResult> Results(Guid id, CancellationToken cancellationToken)
    {
        var page = await PageAsync(id, cancellationToken);
        if (page is null) return NotFound();
        if (page.Run.Status is not (EgressRunStatus.Pulled or EgressRunStatus.PreprocessingFailed or EgressRunStatus.Preprocessed))
            return RedirectToAction(nameof(Resume), new { id });
        return View("Results", page);
    }

    // ---------------------------------------------------------------- Preprocessing

    [HttpGet("runs/{id:guid}/preprocessing")]
    public async Task<IActionResult> Preprocessing(Guid id, CancellationToken cancellationToken)
    {
        var page = await PageAsync(id, cancellationToken);
        if (page is null) return NotFound();
        if (page.Run.Outputs.Sum(o => o.SourceRecordCount) == 0)
            return RedirectToAction(nameof(Results), new { id });
        return View("Preprocessing", page with { StreamUrl = Url.Action(nameof(PreprocessingStream), new { id }) });
    }

    // EventSource can only GET, so the pipeline runs here when JS drives it.
    [HttpGet("runs/{id:guid}/preprocessing/stream")]
    public IResult PreprocessingStream(Guid id, CancellationToken cancellationToken) =>
        // Fully qualified: this controller has its own action named Results, which shadows
        // Microsoft.AspNetCore.Http.Results at an unqualified call site.
        Microsoft.AspNetCore.Http.Results.ServerSentEvents(RunWithNextUrl(id, cancellationToken), eventType: "progress");

    // No-JS fallback: run to completion, then land where the outcome says.
    [HttpPost("runs/{id:guid}/preprocessing")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreprocessingRun(Guid id, CancellationToken cancellationToken)
    {
        EgressProgress? last = null;
        await foreach (var progress in preprocessor.RunAsync(id, cancellationToken)) last = progress;
        return last?.FinalStatus switch
        {
            EgressRunStatus.Preprocessed => RedirectToAction(nameof(Summary), new { id }),
            EgressRunStatus.PreprocessingFailed => RedirectToAction(nameof(Failed), new { id }),
            _ => Home(last?.Message ?? "Preprocessing did not complete.")
        };
    }

    public sealed record ProgressEvent(int Step, int TotalSteps, string StepName, string State, int RecordsIn, int RecordsOut, int FailureCount, bool IsComplete, bool IsError, string Message, string? NextUrl);

    private async IAsyncEnumerable<ProgressEvent> RunWithNextUrl(Guid id, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var p in preprocessor.RunAsync(id, ct))
        {
            var next = p.FinalStatus switch
            {
                EgressRunStatus.Preprocessed => Url.Action(nameof(Summary), new { id }),
                EgressRunStatus.PreprocessingFailed => Url.Action(nameof(Failed), new { id }),
                _ => p.IsComplete ? Url.Action(nameof(Index)) : null
            };
            yield return new ProgressEvent(p.Step, p.TotalSteps, p.StepName, p.State, p.RecordsIn, p.RecordsOut, p.FailureCount, p.IsComplete, p.IsError, p.Message, next);
        }
    }

    [HttpGet("runs/{id:guid}/failed")]
    public async Task<IActionResult> Failed(Guid id, CancellationToken cancellationToken)
    {
        var page = await PageAsync(id, cancellationToken);
        if (page is null) return NotFound();
        if (page.Run.Status != EgressRunStatus.PreprocessingFailed) return RedirectToAction(nameof(Resume), new { id });
        return View("Failed", page);
    }

    // ---------------------------------------------------------------- Summary / preview / transfer

    [HttpGet("runs/{id:guid}/summary")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken cancellationToken)
    {
        var page = await PageAsync(id, cancellationToken);
        if (page is null) return NotFound();
        if (page.Run.Status is not (EgressRunStatus.Preprocessed or EgressRunStatus.TransferFailed or EgressRunStatus.Transferring))
            return RedirectToAction(nameof(Resume), new { id });
        return View("Summary", page with { TransferError = TempData[TransferErrorKey] as string ?? page.Run.TransferFailureReason });
    }

    [HttpGet("runs/{id:guid}/preview/{outputType}")]
    public async Task<IActionResult> Preview(Guid id, EgressOutputType outputType, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(id, cancellationToken);
        var output = run?.Outputs.FirstOrDefault(o => o.OutputType == outputType);
        if (run is null || output?.FileName is null) return NotFound();
        var bytes = await transfer.BuildFileAsync(id, outputType, cancellationToken);
        var lines = System.Text.Encoding.UTF8.GetString(bytes).Split("\r\n");
        return View("Preview", new PreviewViewModel
        {
            RunId = id, OutputType = outputType, FileName = output.FileName,
            Headers = SplitCsv(lines[0]),
            Rows = lines.Skip(1).Where(l => l.Length > 0).Select(SplitCsv).ToList()
        });
    }

    // Preview only: values never contain quotes today (validated digits, names, dates); a quoted
    // value renders with its quotes rather than being mis-split — acceptable for a preview.
    private static IReadOnlyList<string> SplitCsv(string line) => line.Split(',');

    [HttpGet("runs/{id:guid}/download/{outputType}")]
    public async Task<IActionResult> Download(Guid id, EgressOutputType outputType, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(id, cancellationToken);
        var output = run?.Outputs.FirstOrDefault(o => o.OutputType == outputType);
        if (run is null || output?.FileName is null) return NotFound();
        return File(await transfer.BuildFileAsync(id, outputType, cancellationToken), "text/csv", output.FileName);
    }

    [HttpPost("runs/{id:guid}/transfer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(Guid id, CancellationToken cancellationToken)
    {
        var result = await transfer.TransferAsync(id, Actor, cancellationToken);
        switch (result)
        {
            case EgressTransferResult.Transferred:
                return RedirectToAction(nameof(Complete), new { id });
            case EgressTransferResult.Failed failed:
                TempData[TransferErrorKey] = failed.Reason;
                return RedirectToAction(nameof(Summary), new { id });
            // M3: never Complete for an empty approved set — Summary derives the same "nothing to
            // transfer" state from the run's own saved row counts, so no TempData is needed here.
            case EgressTransferResult.NothingToTransfer:
                return RedirectToAction(nameof(Summary), new { id });
            case EgressTransferResult.Refused refused:
                TempData[TransferErrorKey] = Describe((EgressOutputType.RemoveLearners, refused.Blocker)).Replace("Remove learners for this checking window", "This checking window and output type");
                return RedirectToAction(nameof(Summary), new { id });
            default:
                return RedirectToAction(nameof(Resume), new { id });
        }
    }

    [HttpGet("runs/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var page = await PageAsync(id, cancellationToken);
        if (page is null) return NotFound();
        if (page.Run.Status != EgressRunStatus.Transferred) return RedirectToAction(nameof(Resume), new { id });
        return View("Complete", page);
    }

    [HttpPost("runs/{id:guid}/abandon")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Abandon(Guid id, CancellationToken cancellationToken)
    {
        var result = await transfer.AbandonAsync(id, cancellationToken);
        return result switch
        {
            // M1: a Transferring run's own files may have been swept — the banner must say so
            // rather than always claiming nothing was transferred.
            EgressAbandonResult.Abandoned { RemovedFiles.Count: > 0 } abandoned =>
                Home($"The egress run was abandoned. Removed from the target container: {string.Join(", ", abandoned.RemovedFiles)}."),
            EgressAbandonResult.Abandoned =>
                Home("The egress run was abandoned. Nothing was transferred."),
            EgressAbandonResult.AlreadyTransferred =>
                Home("That egress run has already been transferred to LDS and cannot be abandoned."),
            _ => NotFound()
        };
    }

    private async Task<RunPageViewModel?> PageAsync(Guid id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct);
        if (run is null) return null;
        var window = await windows.GetByIdAsync(run.WindowId, ct);
        return new RunPageViewModel
        {
            Run = run,
            WindowTitle = window?.Title ?? "Unknown window",
            WindowType = window?.CheckingWindowType ?? CheckingWindowType.KS4June,
            TargetDescription = blobs.TargetDescription,
            CurrentUserName = currentUser.DisplayName
        };
    }
}
