using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class StorageAdminController(BlobServiceClient blobServiceClient) : Controller
{
    [HttpGet("admin/storage")]
    public async Task<IActionResult> Index()
    {
        var containers = new List<string>();
        await foreach (var item in blobServiceClient.GetBlobContainersAsync())
            containers.Add(item.Name);
        return View(new StorageContainerListViewModel { Containers = containers });
    }

    [HttpGet("admin/storage/{containerName}")]
    public async Task<IActionResult> Container(string containerName)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync())
            return NotFound();

        var blobs = new List<StorageBlobItemViewModel>();
        await foreach (var item in container.GetBlobsAsync())
        {
            blobs.Add(new StorageBlobItemViewModel
            {
                Name = item.Name,
                SizeBytes = item.Properties.ContentLength ?? 0,
                ContentType = item.Properties.ContentType,
                LastModified = item.Properties.LastModified
            });
        }

        return View(new StorageBlobListViewModel { ContainerName = containerName, Blobs = blobs });
    }

    [HttpGet("admin/storage/{containerName}/preview")]
    public async Task<IActionResult> Preview(string containerName, [FromQuery] string blob)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blob);
        if (!await blobClient.ExistsAsync())
            return NotFound();

        var props = await blobClient.GetPropertiesAsync();
        var contentType = props.Value.ContentType;

        string? content = null;
        if (IsTextContent(contentType, blob))
        {
            var response = await blobClient.DownloadContentAsync();
            content = response.Value.Content.ToString();
            if (IsJson(contentType, blob))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    content = System.Text.Json.JsonSerializer.Serialize(
                        doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                }
                catch { /* leave as-is if not valid JSON */ }
            }
        }

        return View(new StorageBlobPreviewViewModel
        {
            ContainerName = containerName,
            BlobName = blob,
            ContentType = contentType,
            Content = content
        });
    }

    [HttpGet("admin/storage/{containerName}/download")]
    public async Task<IActionResult> Download(string containerName, [FromQuery] string blob)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blob);
        if (!await blobClient.ExistsAsync())
            return NotFound();

        var download = await blobClient.DownloadStreamingAsync();
        var fileName = Path.GetFileName(blob);
        var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
        return File(download.Value.Content, contentType, fileName);
    }

    [HttpPost("admin/storage/{containerName}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string containerName, string blobName)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
        return Redirect($"/admin/storage/{containerName}");
    }

    private static bool IsTextContent(string? contentType, string blobName) =>
        contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
        contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
        blobName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        blobName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        blobName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsJson(string? contentType, string blobName) =>
        contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
        blobName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
