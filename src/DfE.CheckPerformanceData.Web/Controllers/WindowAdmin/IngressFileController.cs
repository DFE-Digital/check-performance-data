using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class IngressFileController(ILogger<IngressFileController> logger,
    IWindowService windowService,
    IReadOnlyDictionary<string, BlobServiceClient> blobClients) : Controller
{
    [HttpGet("admin/windows/{id:guid}/ingress-file")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
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
            Files = []
        };

        return View("~/Views/WindowAdmin/IngressFile.cshtml", model);
    }
    
    [HttpGet("admin/windows/{id:guid}/ingress-file/browse")]
    public async Task<IActionResult> Browse(Guid id, string container, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            return RedirectToAction(nameof(Index), new { id });
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
            Files = files
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

    [HttpPost("admin/windows/{id:guid}/ingress-file")]
    [RequestSizeLimit(100_000_000)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(Guid id, string selectedFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            ModelState.AddModelError(nameof(selectedFile), "Select an ingress file");
            return RedirectToAction(nameof(Index), new { id });
        }

        int separatorIndex = selectedFile.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == selectedFile.Length - 1)
        {
            ModelState.AddModelError(nameof(selectedFile), "Select an ingress file");
            return RedirectToAction(nameof(Index), new { id });
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

        string blobName = $"ingress/{ingressFileName}";
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

        window.IngressFile = ingressFileName;
        window.IngressFileChecksum = checksum;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("index", "Summary", new { id = id });
    }
}