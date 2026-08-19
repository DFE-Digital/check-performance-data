using System.Runtime.CompilerServices;
using System.Text;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class ValidateWindowController(IWindowService windowService, ICsvSchemaFileProcessor processor): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Validate.cshtml";

    [HttpGet("admin/windows/{id:guid}/validate")]
    public IActionResult Index(Guid id)
    {
        ValidationViewModel model = new ValidationViewModel
        {
            WindowId = id,
            StreamUrl = Url.Action(nameof(Stream), "ValidateWindow", new { id }),
            PostUrl = Url.Action(nameof(Validate), "ValidateWindow", new { id }),
        };

        return View(PageView, model);
    }

    // Live progress stream (step 1-7). EventSource can only issue GET, so validation runs here;
    // the client opens this on demand from the Start button rather than on page load.
    [HttpGet("admin/windows/{id:guid}/validate/stream")]
    public IResult Stream(Guid id, CancellationToken cancellationToken)
    {
        return Results.ServerSentEvents(Run(id, cancellationToken), eventType: "progress");
    }

    // No-JS fallback: run to completion and render the final summary.
    [HttpPost("admin/windows/{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        ValidationProgress? last = null;
        await foreach (ValidationProgress progress in Run(id, cancellationToken))
        {
            last = progress;
        }

        ValidationViewModel model = new ValidationViewModel
        {
            WindowId = id,
            StreamUrl = Url.Action(nameof(Stream), "ValidateWindow", new { id }),
            PostUrl = Url.Action(nameof(Validate), "ValidateWindow", new { id }),
            ProcessingResult = last is null
                ? null
                : new ProcessingResult(last.RecordsRead, last.FilesWritten, last.ErrorCount, new StringBuilder(last.Message), last.SchoolSummary),
        };

        return View(PageView, model);
    }

    // Drives the processor and, on a clean finish, marks the window validated before the terminal
    // event reaches the caller.
    private async IAsyncEnumerable<ValidationProgress> Run(
        Guid id,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);

        // A Post16 window supplies two datasets (included + non-included); every other type one.
        // They are ingested in a single run so both populations land in one blob per school.
        IReadOnlyList<IngressDataset> datasets = window.AllDatasets
            .Select(d => new IngressDataset(
                d.Name,
                d.IngressFile,
                d.IngressFileChecksum,
                d.SchemaFile,
                d.SchemaFileChecksum,
                d.Included))
            .ToList();

        await foreach (ValidationProgress progress in processor.ProcessAsync(
                           window.Id,
                           datasets,
                           cancellationToken: cancellationToken))
        {
            if (progress is { IsComplete: true, IsError: false })
            {
                window.Validated = true;
                window.ValidatedAt = DateTime.UtcNow;
                await windowService.UpdateAsync(window, cancellationToken);
            }

            yield return progress;
        }
    }
}
