using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize(Roles = WikiConstants.AdminRole)]
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
    public async Task<IActionResult> Container(string account, string containerName, [FromQuery] string? prefix, CancellationToken cancellationToken = default)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken: cancellationToken))
            return NotFound();

        var currentPath = string.IsNullOrWhiteSpace(prefix) ? null : prefix;

        var folders = new List<string>();
        var blobs = new List<StorageBlobItemViewModel>();
        await foreach (var item in container.GetBlobsByHierarchyAsync(delimiter: "/", prefix: currentPath, states: BlobStates.All, traits: BlobTraits.None, cancellationToken: cancellationToken))
        {
            if (item.IsPrefix)
            {
                folders.Add(item.Prefix);
                continue;
            }

            // Hide the zero-byte placeholder that represents the current folder itself.
            if (item.Blob.Name == currentPath)
                continue;

            blobs.Add(new StorageBlobItemViewModel
            {
                Name = item.Blob.Name,
                SizeBytes = item.Blob.Properties.ContentLength ?? 0,
                ContentType = item.Blob.Properties.ContentType,
                LastModified = item.Blob.Properties.LastModified
            });
        }

        return View(new StorageBlobListViewModel
        {
            AccountKey = account,
            AccountDisplayName = GetDisplayName(account),
            ContainerName = containerName,
            Prefix = currentPath,
            ParentPath = GetParentPath(currentPath),
            Folders = folders,
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
    public async Task<IActionResult> Delete(string account, string containerName, string blobName, [FromForm] string? prefix = null)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
        return RedirectToContainer(account, containerName, prefix);
    }

    [HttpPost("admin/storage/{account}/{containerName}/upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(string account, string containerName, List<IFormFile> files, [FromForm] string? prefix, [FromForm] string? folder)
    {
        var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync())
            return NotFound();

        // Files are stored at <current prefix>/<optional new folder>/<file name>.
        // The folder structure exists purely because a real blob lives at that path;
        // blob storage has no standalone folders.
        var targetPrefix = NormalizePrefix(prefix);
        var subFolder = folder?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(subFolder) && !subFolder.Contains(".."))
            targetPrefix += $"{subFolder}/";

        foreach (var file in files ?? [])
        {
            if (file.Length == 0) continue;

            var blobClient = container.GetBlobClient($"{targetPrefix}{Path.GetFileName(file.FileName)}");
            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
            });
        }

        return RedirectToContainer(account, containerName, prefix);
    }

    private IActionResult RedirectToContainer(string account, string containerName, string? prefix)
    {
        var url = $"/admin/storage/{account}/{containerName}";
        if (!string.IsNullOrWhiteSpace(prefix))
            url += $"?prefix={Uri.EscapeDataString(prefix)}";
        return Redirect(url);
    }

    // Ensures a folder prefix is either empty (root) or ends in exactly one "/".
    private static string NormalizePrefix(string? prefix)
    {
        var trimmed = prefix?.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : $"{trimmed}/";
    }

    // Given "foo/bar/" returns "foo/"; given "foo/" or null returns null (root).
    private static string? GetParentPath(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        var trimmed = prefix.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash < 0 ? null : trimmed[..(lastSlash + 1)];
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
