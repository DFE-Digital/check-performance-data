using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class IngressFileController(ILogger<IngressFileController> logger,
    IWindowService windowService,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients) : Controller
{
    // #319: the kind-addressed route names the exercise — see the note on SchemaController.
    // #466 adds the exercise-id route beside it, for the same reason SchemaController does.
    [HttpGet("admin/windows/{id:guid}/{exercise}/ingress-file/{dataset}")]
    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/ingress-file/{dataset}")]
    public async Task<IActionResult> Index(
        Guid id, CheckingExerciseType? exercise, string dataset, CancellationToken cancellationToken, Guid? exerciseId = null)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? owner = window is null ? null : FindOwner(window, exercise, exerciseId);

        if (owner is null || owner.Datasets.All(d => d.Name != dataset))
        {
            return NotFound();
        }

        if (!blobClients.TryGetValue("ingress", out var ingressBlobClient))
        {
            logger.LogWarning("Ingress storage client is not configured");
            return Problem("Ingress storage is not configured.");
        }

        List<string> containers = new List<string>();

        await foreach (BlobContainerItem? container in ingressBlobClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
        {
            containers.Add(container.Name);
        }

        IngressFolderBrowseViewModel model = new IngressFolderBrowseViewModel
        {
            WindowId = id,
            Container = null,
            Folders = containers,
            Files = [],
            Dataset = dataset,
            DatasetLabel = DatasetLabels.For(dataset),
            Exercise = owner.ExerciseType,
            ExerciseId = owner.Id
        };

        return View("~/Views/WindowAdmin/IngressFile.cshtml", model);
    }

    [HttpGet("admin/windows/{id:guid}/{exercise}/ingress-file/{dataset}/browse")]
    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/ingress-file/{dataset}/browse")]
    public async Task<IActionResult> Browse(
        Guid id, CheckingExerciseType? exercise, string dataset, string container, string? path,
        CancellationToken cancellationToken, Guid? exerciseId = null)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? owner = window is null ? null : FindOwner(window, exercise, exerciseId);

        if (owner is null || owner.Datasets.All(d => d.Name != dataset))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(container))
        {
            return RedirectToAction(nameof(Index), new { id, exercise = owner.ExerciseType, exerciseId = owner.Id, dataset });
        }

        if (!blobClients.TryGetValue("ingress", out var ingressBlobClient))
        {
            logger.LogWarning("Ingress storage client is not configured");
            return Problem("Ingress storage is not configured.");
        }

        BlobContainerClient? containerClient = ingressBlobClient.GetBlobContainerClient(container);

        if (!await containerClient.ExistsAsync(cancellationToken))
        {
            return NotFound();
        }

        string? currentPath = string.IsNullOrWhiteSpace(path) ? null : path;
        List<string> folders = new List<string>();
        List<string> files = new List<string>();

        await foreach (var item in containerClient.GetBlobsByHierarchyAsync(
                           delimiter: "/",
                           prefix: currentPath,
                           states: BlobStates.All,
                           traits: BlobTraits.None,
                           cancellationToken: cancellationToken))
        {
            if (item.IsPrefix)
            {
                folders.Add(item.Prefix);
            }

            if (item.IsBlob)
            {
                files.Add(item.Blob.Name);
            }
        }

        IngressFolderBrowseViewModel model = new IngressFolderBrowseViewModel
        {
            WindowId = id,
            Container = container,
            CurrentPath = currentPath,
            ParentPath = GetParentPath(currentPath),
            Folders = folders,
            Files = files,
            Dataset = dataset,
            DatasetLabel = DatasetLabels.For(dataset),
            Exercise = owner.ExerciseType,
            ExerciseId = owner.Id
        };

        return View("~/Views/WindowAdmin/IngressFile.cshtml", model);
    }

    private static string? GetParentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmedPath = path.TrimEnd('/');
        int lastSlashIndex = trimmedPath.LastIndexOf('/');

        if (lastSlashIndex < 0)
        {
            return null;
        }

        return trimmedPath[..(lastSlashIndex + 1)];
    }

    [HttpPost("admin/windows/{id:guid}/{exercise}/ingress-file/{dataset}")]
    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/ingress-file/{dataset}")]
    [RequestSizeLimit(100_000_000)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(
        Guid id, CheckingExerciseType? exercise, string dataset, string selectedFile, CancellationToken cancellationToken,
        Guid? exerciseId = null)
    {
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            ModelState.AddModelError(nameof(selectedFile), "Select an ingress file");
            return RedirectToAction(nameof(Index), new { id, exercise, exerciseId, dataset });
        }

        int separatorIndex = selectedFile.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == selectedFile.Length - 1)
        {
            ModelState.AddModelError(nameof(selectedFile), "Select an ingress file");
            return RedirectToAction(nameof(Index), new { id, exercise, exerciseId, dataset });
        }

        string sourceContainer = selectedFile[..separatorIndex];
        string sourceBlobName = selectedFile[(separatorIndex + 1)..];
        string ingressFileName = sourceBlobName.Split('/').Last();

        if (!blobClients.TryGetValue("ingress", out var ingressBlobClient))
        {
            logger.LogWarning("Ingress storage client is not configured");
            return Problem("Ingress storage is not configured.");
        }

        if (!blobClients.TryGetValue("app", out var appBlobClient))
        {
            logger.LogWarning("App storage client is not configured");
            return Problem("App storage is not configured.");
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

        BlobClient? sourceBlob = ingressBlobClient.GetBlobContainerClient(sourceContainer).GetBlobClient(sourceBlobName);

        if (!await sourceBlob.ExistsAsync(cancellationToken))
        {
            logger.LogWarning("Ingress file {SelectedFile} no longer exists", selectedFile);
            return NotFound();
        }

        Response<BlobDownloadStreamingResult>? download = await sourceBlob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        using var buffer = new MemoryStream();
        await download.Value.Content.CopyToAsync(buffer, cancellationToken);

        buffer.Position = 0;
        string checksum = Convert.ToHexString(SHA256.HashData(buffer));

        BlobContainerClient? destinationContainer = appBlobClient.GetBlobContainerClient(id.ToString());
        await destinationContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // #466: stored at a path unique to this exercise and dataset, not a name relative to a
        // shared ingress/ root — two exercises can both have a slot called "main".
        string blobName = CheckingExerciseBlobPaths.DefinitionFile(owner.Id, target.Id, ingressFileName);
        BlobClient? destinationBlob = destinationContainer.GetBlobClient(blobName);

        buffer.Position = 0;
        await destinationBlob.UploadAsync(
            buffer,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = download.Value.Details.ContentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
            },
            cancellationToken);

        // The dataset row stores the complete blob name from here on. A row written before this
        // change stores a name relative to the old ingress/ root, and CheckingExerciseBlobPaths.
        // IngressBlobName reads both shapes without anything having to move.
        target.IngressFile = blobName;
        target.IngressFileChecksum = checksum;

        // Legacy scalar columns mirror the first dataset for one release (rollback safety).
        if (target.SortOrder == 0)
        {
            window.IngressFile = blobName;
            window.IngressFileChecksum = checksum;
        }

        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("index", "Summary", new { id = id });
    }

    // Addressed by exercise id when the caller has one (#466); otherwise by kind, for every
    // caller still on the #319 shape.
    private static CheckingExerciseDto? FindOwner(CheckingWindowDto window, CheckingExerciseType? exercise, Guid? exerciseId) =>
        exerciseId is not null
            ? window.Exercises.SingleOrDefault(e => e.Id == exerciseId)
            : window.FindExercise(exercise);
}
