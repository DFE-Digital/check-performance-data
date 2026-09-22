using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using System.Runtime.CompilerServices;
using System.Text;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Runs one checking exercise's ingress + schema pair and, on a clean finish, stamps that exercise
/// validated (#319).
/// </summary>
/// <remarks>
/// The route names the exercise rather than looping over every exercise in one run. A loop would
/// have to emit several terminal progress events down a stream whose client expects one, and would
/// stop an admin revalidating a single exercise after replacing one of its files. Running one at a
/// time keeps each run exactly the shape the processor and the progress stream already handle, and
/// is what makes "a window is usable while another exercise is still unvalidated" true rather than
/// merely allowed.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public class ValidateWindowController(IWindowService windowService, ICsvSchemaFileProcessor processor): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Validate.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate")]
    public async Task<IActionResult> Index(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exerciseId);

        if (target is null)
        {
            return NotFound();
        }

        // A display-only exercise is a real row on this window, just not ready to validate — 400,
        // not 404. (Close treats the same exercise as 404 instead: it has no journeys, so there is
        // nothing to sweep — see the note on CloseExerciseController.)
        // Two distinct reasons collapse into one guard here: a display-only exercise has no blob
        // prefix until slice 2 (#466), and a kind exercise may simply be missing a required
        // ingress or schema file. Summary disables the button either way; this is the guard for a
        // typed URL. Run() below tells the two apart in its own error message.
        if (!target.CanValidate)
        {
            return BadRequest("This exercise cannot be validated yet.");
        }

        return View(PageView, Model(id, exerciseId, target.Name));
    }

    // Live progress stream (step 1-7). EventSource can only issue GET, so validation runs here;
    // the client opens this on demand from the Start button rather than on page load.
    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate/stream")]
    public IResult Stream(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        return Results.ServerSentEvents(Run(id, exerciseId, cancellationToken), eventType: "progress");
    }

    // No-JS fallback: run to completion and render the final summary.
    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        ValidationProgress? last = null;
        await foreach (ValidationProgress progress in Run(id, exerciseId, cancellationToken))
        {
            last = progress;
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        string label = window?.FindExercise(exerciseId)?.Name ?? "exercise";

        ValidationViewModel model = Model(id, exerciseId, label);
        model.ProcessingResult = last is null
            ? null
            : new ProcessingResult(last.RecordsRead, last.FilesWritten, last.ErrorCount, new StringBuilder(last.Message), last.SchoolSummary);

        return View(PageView, model);
    }

    private ValidationViewModel Model(Guid id, Guid exerciseId, string label) => new()
    {
        WindowId = id,
        ExerciseLabel = label,
        StreamUrl = Url.Action(nameof(Stream), "ValidateWindow", new { id, exerciseId }),
        PostUrl = Url.Action(nameof(Validate), "ValidateWindow", new { id, exerciseId }),
        CancelUrl = Url.Action("Index", "Summary", new { id })
    };

    // Drives the processor for one exercise and, on a clean finish, stamps that exercise validated
    // before the terminal event reaches the caller.
    private async IAsyncEnumerable<ValidationProgress> Run(
        Guid id,
        Guid exerciseId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            yield return new ValidationProgress(
                Phase: "error",
                Message: "This checking window no longer exists.",
                RecordsRead: 0, RecordsProcessed: 0, FilesWritten: 0, ErrorCount: 1,
                IsComplete: true, IsError: true);
            yield break;
        }

        CheckingExerciseDto? target = window.FindExercise(exerciseId);

        if (target is null)
        {
            yield return new ValidationProgress(
                Phase: "error",
                Message: "This window does not run that exercise.",
                RecordsRead: 0, RecordsProcessed: 0, FilesWritten: 0, ErrorCount: 1,
                IsComplete: true, IsError: true);
            yield break;
        }

        // CanValidate fails for two different reasons, and the admin needs to know which: a
        // display-only exercise has no kind to pick a data store with, while a kind exercise may
        // simply be missing one of its required files. Telling both the same message sends an
        // admin who is only short a file hunting for a kind setting that does not exist.
        if (target.ExerciseType is null)
        {
            yield return new ValidationProgress(
                Phase: "error",
                Message: $"{target.Name} cannot be validated yet: it has no data store until its kind is known.",
                RecordsRead: 0, RecordsProcessed: 0, FilesWritten: 0, ErrorCount: 1,
                IsComplete: true, IsError: true);
            yield break;
        }

        if (!target.HasRequiredFiles)
        {
            yield return new ValidationProgress(
                Phase: "error",
                Message: $"{target.Name} does not yet have every required ingress and schema file.",
                RecordsRead: 0, RecordsProcessed: 0, FilesWritten: 0, ErrorCount: 1,
                IsComplete: true, IsError: true);
            yield break;
        }

        // A Post16 pupil-data exercise supplies two datasets (included + non-included) and a
        // results enquiry supplies one per source file in the results feed; every other pupil-data
        // exercise supplies a single dataset. They are ingested in a single run so every population
        // lands in one blob per school — which is why a run is per exercise and not per dataset.
        IReadOnlyList<IngressDataset> datasets = target.DatasetsToIngest
            .Select(d => new IngressDataset(
                d.Name,
                d.IngressFile,
                d.IngressFileChecksum,
                d.SchemaFile,
                d.SchemaFileChecksum,
                d.Included,
                d.SourceFile))
            .ToList();

        // CanValidate guarantees a kind above, so the processor (which selects its blob prefix by
        // CheckingExerciseType) always has one to read.
        await foreach (ValidationProgress progress in processor.ProcessAsync(
                           window.Id,
                           target.ExerciseType!.Value,
                           datasets,
                           cancellationToken: cancellationToken))
        {
            if (progress is { IsComplete: true, IsError: false })
            {
                // Stamped with the checksums of the files this run actually read, so replacing one
                // afterwards leaves a stamp the summary page can show as stale rather than as a
                // clean bill of health for data nobody validated.
                target.ValidatedAt = DateTime.UtcNow;
                target.ValidatedIngressChecksum = target.CurrentIngressChecksum;
                target.ValidatedSchemaChecksum = target.CurrentSchemaChecksum;
                await windowService.UpdateAsync(window, cancellationToken);
            }

            yield return progress;
        }
    }
}
