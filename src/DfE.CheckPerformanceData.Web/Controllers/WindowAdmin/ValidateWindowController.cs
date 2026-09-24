using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using System.Runtime.CompilerServices;
using System.Text;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
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
public class ValidateWindowController(
    IWindowService windowService,
    ICsvSchemaFileProcessor processor,
    ICheckingExerciseIngress ingress,
    ICurrentUserService currentUser): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Validate.cshtml";

    [HttpGet("admin/windows/{id:guid}/{exercise}/validate")]
    public IActionResult Index(Guid id, CheckingExerciseType exercise)
    {
        return View(PageView, Model(id, exercise));
    }

    // Live progress stream (step 1-7). EventSource can only issue GET, so validation runs here;
    // the client opens this on demand from the Start button rather than on page load.
    [HttpGet("admin/windows/{id:guid}/{exercise}/validate/stream")]
    public IResult Stream(Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        return Results.ServerSentEvents(Run(id, exercise, cancellationToken), eventType: "progress");
    }

    // No-JS fallback: run to completion and render the final summary.
    [HttpPost("admin/windows/{id:guid}/{exercise}/validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        ValidationProgress? last = null;
        await foreach (ValidationProgress progress in Run(id, exercise, cancellationToken))
        {
            last = progress;
        }

        ValidationViewModel model = Model(id, exercise);
        model.ProcessingResult = last is null
            ? null
            : new ProcessingResult(last.RecordsRead, last.FilesWritten, last.ErrorCount, new StringBuilder(last.Message), last.SchoolSummary);

        return View(PageView, model);
    }

    // --- Exercise-id addressed routes (#466 slice 2) ---
    //
    // These sit beside the kind-addressed routes above rather than replacing them, so nothing
    // already linking to `{exercise}/validate` breaks. They run through ICheckingExerciseIngress
    // (Task 11), which stamps the exercise itself on a clean finish, so unlike the kind-addressed
    // path above there is no manual stamping here. An exercise id that does not belong to the
    // window named in the route is refused with NotFound() before any streaming starts — an admin
    // must not be able to drive another window's exercise through this window's page.

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate")]
    public async Task<IActionResult> IndexForExercise(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        CheckingExerciseDto? target = await ResolveExerciseAsync(id, exerciseId, cancellationToken);

        return target is null ? NotFound() : View(PageView, ModelForExercise(id, exerciseId, target.ExerciseType));
    }

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate/stream")]
    public async Task<IResult> StreamForExercise(
        Guid id, Guid exerciseId, bool clearExistingFiles, CancellationToken cancellationToken)
    {
        CheckingExerciseDto? target = await ResolveExerciseAsync(id, exerciseId, cancellationToken);

        if (target is null)
        {
            return Results.NotFound();
        }

        return Results.ServerSentEvents(
            ingress.ProcessAsync(exerciseId, clearExistingFiles, cancellationToken, currentUser.Email), eventType: "progress");
    }

    // No-JS fallback for the exercise-id route: run the one named exercise to completion through
    // ICheckingExerciseIngress and render the final summary. One exercise, one run, one terminal
    // event — never a loop over the window's exercises.
    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        Guid id, Guid exerciseId, bool clearExistingFiles, CancellationToken cancellationToken)
    {
        CheckingExerciseDto? target = await ResolveExerciseAsync(id, exerciseId, cancellationToken);

        if (target is null)
        {
            return NotFound();
        }

        ValidationProgress? last = null;
        await foreach (ValidationProgress progress in ingress.ProcessAsync(exerciseId, clearExistingFiles, cancellationToken, currentUser.Email))
        {
            last = progress;
        }

        ValidationViewModel model = ModelForExercise(id, exerciseId, target.ExerciseType);
        model.ProcessingResult = last is null
            ? null
            : new ProcessingResult(last.RecordsRead, last.FilesWritten, last.ErrorCount, new StringBuilder(last.Message), last.SchoolSummary);

        return View(PageView, model);
    }

    private async Task<CheckingExerciseDto?> ResolveExerciseAsync(
        Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        return window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
    }

    private ValidationViewModel Model(Guid id, CheckingExerciseType exercise) => new()
    {
        WindowId = id,
        ExerciseLabel = ExerciseLabels.For(exercise),
        StreamUrl = Url.Action(nameof(Stream), "ValidateWindow", new { id, exercise }),
        PostUrl = Url.Action(nameof(Validate), "ValidateWindow", new { id, exercise }),
        CancelUrl = Url.Action("Index", "Summary", new { id })
    };

    private ValidationViewModel ModelForExercise(Guid id, Guid exerciseId, CheckingExerciseType? exercise) => new()
    {
        WindowId = id,
        ExerciseLabel = ExerciseLabels.For(exercise),
        StreamUrl = Url.Action(nameof(StreamForExercise), "ValidateWindow", new { id, exerciseId }),
        PostUrl = Url.Action(nameof(Run), "ValidateWindow", new { id, exerciseId }),
        CancelUrl = Url.Action("Index", "Summary", new { id }),
        ShowClearExistingFiles = true
    };

    // Drives the processor for one exercise and, on a clean finish, stamps that exercise validated
    // before the terminal event reaches the caller.
    private async IAsyncEnumerable<ValidationProgress> Run(
        Guid id,
        CheckingExerciseType exercise,
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

        CheckingExerciseDto? target = window.FindExercise(exercise);

        if (target is null)
        {
            yield return new ValidationProgress(
                Phase: "error",
                Message: $"This window does not run {ExerciseLabels.For(exercise)}.",
                RecordsRead: 0, RecordsProcessed: 0, FilesWritten: 0, ErrorCount: 1,
                IsComplete: true, IsError: true);
            yield break;
        }

        // An exercise on the exercise-id storage is run through the ingress, which writes a new
        // release and makes it live. The processor call below writes the kind-based paths, which
        // only a legacy row's readers use. The admin exercise page links here for every exercise
        // that has a kind, so without this a new row's run went to paths nothing reads.
        if (target.UsesExerciseStorage)
        {
            await foreach (ValidationProgress progress in ingress.ProcessAsync(
                               target.Id, cancellationToken: cancellationToken, publishedBy: currentUser.Email))
            {
                yield return progress;
            }
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

        await foreach (ValidationProgress progress in processor.ProcessAsync(
                           window.Id,
                           exercise,
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
