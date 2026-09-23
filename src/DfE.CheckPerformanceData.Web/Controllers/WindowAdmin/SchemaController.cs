using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public class SchemaController(
    ILogger<SchemaController> logger,
    IWindowService windowService,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients)
    : Controller
{

    private const string PageView = "~/Views/WindowAdmin/Schema.cshtml";

    // #319: the kind-addressed route names the exercise. A dataset belongs to the exercise that
    // consumes it, and dataset names are only unique within one — "pupils" could belong to either
    // once a second exercise gains slots. #466 adds the exercise-id route beside it, because two
    // exercises of the same kind (a replacement release) can both have a slot with the same name,
    // and a display-only exercise has no kind to route by at all.
    [HttpGet("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/schema-file/{dataset}")]
    public async Task<IActionResult> Index(
        Guid id, CheckingExerciseType? exercise, string dataset, CancellationToken cancellationToken, Guid? exerciseId = null)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        CheckingExerciseDto? owner = FindOwner(window, exercise, exerciseId);
        CheckingWindowDatasetDto? target = owner?.Datasets.SingleOrDefault(d => d.Name == dataset);

        if (owner is null || target is null)
        {
            return NotFound();
        }

        SchemaItem model = new SchemaItem()
        {
            WindowId = window.Id,
            SchemaFile = target.SchemaFile,
            Dataset = target.Name,
            DatasetLabel = DatasetLabels.For(target.Name),
            PostUrl = Url.Action("Submit", "Schema",
                new { id = window.Id, exercise = owner.ExerciseType, exerciseId = owner.Id, dataset = target.Name }),
            CancelUrl = Url.Action("Index", "Summary", new { id = window.Id }),
        };
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/schema-file/{dataset}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        Guid id, CheckingExerciseType? exercise, string dataset, SchemaItem model, CancellationToken cancellationToken,
        Guid? exerciseId = null)
    {
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        if (model.Schema is null || model.Schema.Length == 0)
        {
            ModelState.AddModelError(nameof(model.Schema), "Select a JSON schema file");
            return View(PageView, model);
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }

        CheckingExerciseDto? owner = FindOwner(window, exercise, exerciseId);
        CheckingWindowDatasetDto? target = owner?.Datasets.SingleOrDefault(d => d.Name == dataset);

        if (owner is null || target is null)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await using (Stream upload = model.Schema.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        if (!JsonSchemaValidator.TryValidate(buffer, out string? validationError))
        {
            ModelState.AddModelError(nameof(model.Schema), validationError ?? "The file is not a valid JSON schema");
            return View(PageView, model);
        }

        if (!blobClients.TryGetValue("app", out var appBlobClient))
        {
            logger.LogWarning("App storage client is not configured");
            return Problem("App storage is not configured.");
        }

        buffer.Position = 0;
        string checksum = Convert.ToHexString(SHA256.HashData(buffer));

        BlobContainerClient destinationContainer = appBlobClient.GetBlobContainerClient(id.ToString());
        await destinationContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // #466: stored at a path unique to this exercise and dataset, not a name relative to a
        // shared schema/ root — two exercises can both have a slot called "main".
        string schemaFileName = Path.GetFileName(model.Schema.FileName);
        string blobName = CheckingExerciseBlobPaths.DefinitionFile(owner.Id, target.Id, schemaFileName);
        BlobClient destinationBlob = destinationContainer.GetBlobClient(blobName);

        buffer.Position = 0;
        await destinationBlob.UploadAsync(
            buffer,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
            },
            cancellationToken);

        // The dataset row stores the complete blob name from here on. A row written before this
        // change stores a name relative to the old schema/ root, and CheckingExerciseBlobPaths.
        // SchemaBlobName reads both shapes without anything having to move.
        target.SchemaFile = blobName;
        target.SchemaFileChecksum = checksum;

        // Legacy scalar columns mirror the first dataset for one release (rollback safety).
        if (target.SortOrder == 0)
        {
            window.SchemaFile = blobName;
            window.SchemaFileChecksum = checksum;
        }

        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    // Addressed by exercise id when the caller has one (#466 — the only way to name a
    // display-only exercise, or one of several releases of the same kind); otherwise by kind, for
    // every route and caller still on the #319 shape.
    private static CheckingExerciseDto? FindOwner(CheckingWindowDto window, CheckingExerciseType? exercise, Guid? exerciseId) =>
        exerciseId is not null
            ? window.Exercises.SingleOrDefault(e => e.Id == exerciseId)
            : window.FindExercise(exercise);
}
