using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Blob-storage browser and per-blob preview / download / delete. Gated by the storage-admin
// section grant.
[RequireAdminSection(AdminNavKeys.StorageAdmin)]
public sealed class StorageAdminController(IReadOnlyDictionary<string, BlobServiceClient> storageAccounts) : Controller
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        ["app"] = "App Storage",
        ["ingress"] = "Ingress Storage",
    };

    [HttpGet("admin/storage")]
    public IActionResult Index()
    {
        var accounts = storageAccounts.Keys
            .Select(k => new StorageAccountViewModel { Key = k, DisplayName = GetDisplayName(k) })
            .ToList();
        return View(new StorageAccountListViewModel { Accounts = accounts });
    }

    [HttpGet("admin/storage/{account}")]
    public async Task<IActionResult> Containers(string account)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var containers = new List<string>();
        await foreach (var item in client.GetBlobContainersAsync())
            containers.Add(item.Name);

        return View(new StorageContainerListViewModel
        {
            AccountKey = account,
            AccountDisplayName = GetDisplayName(account),
            Containers = containers,
        });
    }

    [HttpGet("admin/storage/{account}/{containerName}")]
    public async Task<IActionResult> Container(string account, string containerName)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
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

        return View(new StorageBlobListViewModel
        {
            AccountKey = account,
            AccountDisplayName = GetDisplayName(account),
            ContainerName = containerName,
            Blobs = blobs,
        });
    }

    [HttpGet("admin/storage/{account}/{containerName}/preview")]
    public async Task<IActionResult> Preview(string account, string containerName, [FromQuery] string blob)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
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
            AccountKey = account,
            AccountDisplayName = GetDisplayName(account),
            ContainerName = containerName,
            BlobName = blob,
            ContentType = contentType,
            Content = content
        });
    }

    [HttpGet("admin/storage/{account}/{containerName}/download")]
    public async Task<IActionResult> Download(string account, string containerName, [FromQuery] string blob)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blob);
        if (!await blobClient.ExistsAsync())
            return NotFound();

        var download = await blobClient.DownloadStreamingAsync();
        var fileName = Path.GetFileName(blob);
        var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
        return File(download.Value.Content, contentType, fileName);
    }

    [HttpPost("admin/storage/{account}/{containerName}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string account, string containerName, string blobName)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
        return Redirect($"/admin/storage/{account}/{containerName}");
    }

    private BlobServiceClient? GetClient(string account) =>
        storageAccounts.TryGetValue(account, out var client) ? client : null;

    private static string GetDisplayName(string account) =>
        DisplayNames.TryGetValue(account, out var name) ? name : account;

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
