using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public class SchemaController(
    ILogger<SchemaController> logger,
    IWindowService windowService,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/Schema.cshtml";

    [HttpGet("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    public async Task<IActionResult> Index(Guid id, CheckingExerciseType exercise, string dataset,
        CancellationToken cancellationToken, Guid? exerciseId = null, bool returnToExercise = false)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var owner = FindExercise(window, exercise, exerciseId);
        var target = owner?.Datasets.SingleOrDefault(d => d.Name == dataset);
        if (window is null || owner is null || target is null) return NotFound();

        var model = new SchemaItem { WindowId = id };
        Decorate(model, window, owner, target, returnToExercise, await SchemaSources(cancellationToken));
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, CheckingExerciseType exercise, string dataset, SchemaItem model,
        CancellationToken cancellationToken, Guid? exerciseId = null, bool returnToExercise = false)
    {
        if (id != model.WindowId) return BadRequest();
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var owner = FindExercise(window, exercise, exerciseId);
        var target = owner?.Datasets.SingleOrDefault(d => d.Name == dataset);
        if (window is null || owner is null || target is null) return NotFound();

        var sources = await SchemaSources(cancellationToken);
        Decorate(model, window, owner, target, returnToExercise, sources);
        var hasUpload = model.Schema is { Length: > 0 };
        if (!hasUpload && model.ExistingSchemaDatasetId is null)
            ModelState.AddModelError(nameof(model.Schema), "Select a JSON schema file or choose an existing schema");
        if (hasUpload && model.ExistingSchemaDatasetId is not null)
            ModelState.AddModelError(nameof(model.ExistingSchemaDatasetId), "Choose an existing schema or upload a file, not both");
        var source = sources.SingleOrDefault(s => s.DatasetId == model.ExistingSchemaDatasetId);
        if (model.ExistingSchemaDatasetId is not null && source is null)
            ModelState.AddModelError(nameof(model.ExistingSchemaDatasetId), "Select an available schema");
        if (!ModelState.IsValid) return View(PageView, model);

        using var buffer = new MemoryStream();
        string filename;
        if (source is not null)
        {
            if (!blobClients.TryGetValue("app", out var sourceClient))
                return Problem("App storage is not configured.");
            var sourceBlob = sourceClient.GetBlobContainerClient(source.WindowId.ToString())
                .GetBlobClient(CheckingExerciseBlobPaths.SchemaBlobName(source.Path));
            if (!await sourceBlob.ExistsAsync(cancellationToken))
            {
                ModelState.AddModelError(nameof(model.ExistingSchemaDatasetId), "This schema is no longer available. Choose another schema or upload a file");
                return View(PageView, model);
            }
            await sourceBlob.DownloadToAsync(buffer, cancellationToken);
            filename = Path.GetFileName(source.Path);
        }
        else
        {
            await using var upload = model.Schema!.OpenReadStream();
            await upload.CopyToAsync(buffer, cancellationToken);
            filename = Path.GetFileName(model.Schema.FileName);
        }

        buffer.Position = 0;
        if (!JsonSchemaValidator.TryValidate(buffer, out var error))
        {
            ModelState.AddModelError(source is null ? nameof(model.Schema) : nameof(model.ExistingSchemaDatasetId),
                error ?? "The file is not a valid JSON schema");
            return View(PageView, model);
        }

        if (!blobClients.TryGetValue("app", out var appClient))
        {
            logger.LogWarning("App storage client is not configured");
            return Problem("App storage is not configured.");
        }

        buffer.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(buffer));
        // A schema must never overwrite the CSV if a supplier gave both files the same name.
        if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) filename += ".json";
        var schemaPath = CheckingExerciseBlobPaths.DefinitionFile(owner.Id, target.Id, filename);
        if (schemaPath.Length > 255)
        {
            ModelState.AddModelError(nameof(model.Schema), "Use a shorter schema filename");
            return View(PageView, model);
        }
        var destination = appClient.GetBlobContainerClient(id.ToString());
        await destination.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        buffer.Position = 0;
        await destination.GetBlobClient(schemaPath).UploadAsync(buffer, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
        }, cancellationToken);

        target.SchemaFile = schemaPath;
        target.SchemaFileChecksum = checksum;
        if (target.SortOrder == 0)
        {
            window.SchemaFile = schemaPath;
            window.SchemaFileChecksum = checksum;
        }
        await windowService.UpdateAsync(window, cancellationToken);
        return returnToExercise
            ? RedirectToAction("Edit", "EditCheckingExercise", new { id, exerciseId = owner.Id }, "data-files")
            : RedirectToAction("Index", "Summary", new { id });
    }

    private async Task<List<SchemaSource>> SchemaSources(CancellationToken cancellationToken)
    {
        var data = await windowService.GetAllDataAsync(cancellationToken);
        return data?.Windows.SelectMany(w => w.Exercises.SelectMany(e => e.Datasets
            .Where(d => d.Id != Guid.Empty && !string.IsNullOrWhiteSpace(d.SchemaFile))
            .Select(d => new SchemaSource(w.Id, d.Id, d.SchemaFile,
                $"{w.Title} / {e.Name ?? ExerciseLabels.For(e.ExerciseType)} / {DatasetLabels.For(d.Name)} / {Path.GetFileName(d.SchemaFile)}"))))
            .OrderBy(s => s.Label).ToList() ?? [];
    }

    private void Decorate(SchemaItem model, CheckingWindowDto window, CheckingExerciseDto owner,
        CheckingWindowDatasetDto target, bool returnToExercise, List<SchemaSource> sources)
    {
        model.SchemaFile = target.SchemaFile;
        model.Dataset = target.Name;
        model.DatasetLabel = DatasetLabels.For(target.Name);
        model.ExistingSchemas = sources.Select(s => new ExistingSchemaOption(s.DatasetId, s.Label)).ToList();
        model.PostUrl = Url.Action("Submit", "Schema", new
        {
            id = window.Id, exercise = owner.ExerciseType, dataset = target.Name, exerciseId = owner.Id, returnToExercise
        });
        model.CancelUrl = returnToExercise
            ? Url.Action("Edit", "EditCheckingExercise", new { id = window.Id, exerciseId = owner.Id })
            : Url.Action("Index", "Summary", new { id = window.Id });
    }

    private static CheckingExerciseDto? FindExercise(CheckingWindowDto? window, CheckingExerciseType type, Guid? id)
    {
        var matches = window?.Exercises.Where(e => e.ExerciseType == type && (id is null || e.Id == id)).Take(2).ToList();
        return matches?.Count == 1 ? matches[0] : null;
    }

    private sealed record SchemaSource(Guid WindowId, Guid DatasetId, string Path, string Label);
}
