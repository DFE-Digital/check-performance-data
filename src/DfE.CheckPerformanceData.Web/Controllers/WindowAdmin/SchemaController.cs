using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class SchemaController(
    ILogger<SchemaController> logger,
    IWindowService windowService,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients)
    : Controller
{

    private const string PageView = "~/Views/WindowAdmin/Schema.cshtml";

    // #319: the route names the exercise. A dataset belongs to the exercise that consumes it,
    // and dataset names are only unique within one — "pupils" could belong to either once a
    // second exercise gains slots.
    [HttpGet("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    public async Task<IActionResult> Index(Guid id, CheckingExerciseType exercise, string dataset, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        CheckingWindowDatasetDto? target = FindDataset(window, exercise, dataset);

        if (target is null)
        {
            return NotFound();
        }

        SchemaItem model = new SchemaItem()
        {
            WindowId = window.Id,
            SchemaFile = target.SchemaFile,
            Dataset = target.Name,
            DatasetLabel = DatasetLabels.For(target.Name),
            PostUrl = Url.Action("Submit", "Schema", new { id = window.Id, exercise, dataset = target.Name }),
        };
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/schema-file/{dataset}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, CheckingExerciseType exercise, string dataset, SchemaItem model, CancellationToken cancellationToken)
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

        string schemaFileName = Path.GetFileName(model.Schema.FileName);
        string blobName = $"schema/{schemaFileName}";
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

        CheckingWindowDatasetDto? target = FindDataset(window, exercise, dataset);

        if (target is null)
        {
            return NotFound();
        }

        target.SchemaFile = schemaFileName;
        target.SchemaFileChecksum = checksum;

        // Legacy scalar columns mirror the first dataset for one release (rollback safety).
        if (target.SortOrder == 0)
        {
            window.SchemaFile = schemaFileName;
            window.SchemaFileChecksum = checksum;
        }

        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    private static CheckingWindowDatasetDto? FindDataset(
        CheckingWindowDto window, CheckingExerciseType exercise, string dataset) =>
        window.FindExercise(exercise)?.Datasets.SingleOrDefault(d => d.Name == dataset);
}
