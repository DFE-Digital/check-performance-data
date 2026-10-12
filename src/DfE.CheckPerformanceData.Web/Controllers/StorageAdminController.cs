using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Blob-storage browser and per-blob preview / download / delete. Gated by the storage-browser
// section grant. It sits in the nav under Danger zone (it deletes blobs), in every environment.
[RequireAdminSection(AdminNavKeys.StorageBrowser)]
public sealed class StorageAdminController(
    IReadOnlyDictionary<string, BlobServiceClient> storageAccounts,
    IOptions<StorageBrowserOptions> browserOptions,
    ILogger<StorageAdminController> logger) : Controller
{
    // The keyring and anything else configured as secret. Every route consults this before it
    // resolves a container, so a protected blob is never opened, written or removed — not opened
    // and then withheld. Kept in one place because six separate guards drift apart.
    private readonly HashSet<string> _protectedContainers =
        new(browserOptions.Value.ProtectedContainers ?? [], StringComparer.OrdinalIgnoreCase);

    // 404 rather than 403, matching what a non-granted admin section returns: a refusal that
    // confirms the container exists is a smaller leak than the contents, but it is still a leak.
    internal const int PreviewMaxBytes = 256 * 1024;

    // Drops a UTF-8 character cut in half by the end of a partial read, so the preview never ends in
    // a replacement character.
    internal static int CompleteUtf8Length(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        for (var back = 1; back <= 3 && back <= end; back++)
        {
            var b = bytes[end - back];
            if ((b & 0b1100_0000) == 0b1000_0000) continue; // continuation byte, keep looking for its lead
            var needed = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return needed > back ? end - back : end;
        }
        return end;
    }

    internal const BlobStates ListedStates = BlobStates.All & ~BlobStates.Uncommitted;

    private bool IsProtected(string containerName) => _protectedContainers.Contains(containerName);

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
        {
            if (IsProtected(item.Name)) continue;
            containers.Add(item.Name);
        }

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
        if (IsProtected(containerName)) return NotFound();
        if (!StorageBlobNames.IsValidPrefix(prefix)) return NotFound();

                var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken: cancellationToken))
            return NotFound();

        var currentPath = string.IsNullOrWhiteSpace(prefix) ? null : prefix;

        var folders = new List<string>();
        var blobs = new List<StorageBlobItemViewModel>();
        // Uncommitted blobs (blocks staged by a chunked upload that was cancelled, failed or is still
        // running) have no content and cannot be read, previewed or deleted. Azure keeps them for up
        // to 7 days, so leave them out rather than list a ghost 0 B row.
        await foreach (var item in container.GetBlobsByHierarchyAsync(delimiter: "/", prefix: currentPath, states: ListedStates, traits: BlobTraits.None, cancellationToken: cancellationToken))
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
            ChunkBytes = browserOptions.Value.ChunkBytes,
            MaxUploadBytes = browserOptions.Value.MaxUploadBytes,
        });
    }

    [HttpGet("admin/storage/{account}/{containerName}/preview")]
    public async Task<IActionResult> Preview(string account, string containerName, [FromQuery] string blob)
    {
        if (IsProtected(containerName)) return NotFound();
        if (!StorageBlobNames.IsValidBlobName(blob)) return NotFound();

                var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blob);
        if (!await blobClient.ExistsAsync())
            return NotFound();

        var props = await blobClient.GetPropertiesAsync();
        var contentType = props.Value.ContentType;

        string? content = null;
        var isPartial = false;
        if (IsTextContent(contentType, blob))
        {
            // Read only the start: a blob can now be up to 2GB, and turning all of it into a string
            // would exhaust the pod's memory.
            var length = props.Value.ContentLength;
            isPartial = length > PreviewMaxBytes;
            if (length == 0)
            {
                content = string.Empty;
            }
            else
            {
                var response = await blobClient.DownloadContentAsync(
                    new BlobDownloadOptions { Range = new HttpRange(0, PreviewMaxBytes) });
                var bytes = response.Value.Content.ToMemory();
                if (isPartial) bytes = bytes[..CompleteUtf8Length(bytes.Span)];
                content = System.Text.Encoding.UTF8.GetString(bytes.Span);
            }

            // A cut-off document is not valid JSON, so only tidy one that was read whole.
            if (!isPartial && IsJson(contentType, blob))
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
            Content = content,
            IsPartial = isPartial
        });
    }

    [HttpGet("admin/storage/{account}/{containerName}/download")]
    public async Task<IActionResult> Download(string account, string containerName, [FromQuery] string blob)
    {
        if (IsProtected(containerName)) return NotFound();
        if (!StorageBlobNames.IsValidBlobName(blob)) return NotFound();

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
        if (IsProtected(containerName)) return NotFound();
        if (!StorageBlobNames.IsValidBlobName(blobName) || !StorageBlobNames.IsValidPrefix(prefix)) return NotFound();

                var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
        return RedirectToContainer(account, containerName, prefix);
    }

    // Kestrel's MaxRequestBodySize is disabled app-wide, so the plain form needs its own ceiling.
    // 128 MB is ASP.NET Core's multipart default made explicit; in deployed environments the
    // ingress controller refuses anything over 50 MB long before this is reached (#568).
    [HttpPost("admin/storage/{account}/{containerName}/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(128L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 128L * 1024 * 1024)]
    public async Task<IActionResult> Upload(string account, string containerName, List<IFormFile> files, [FromForm] string? prefix, [FromForm] string? folder)
    {
        if (IsProtected(containerName)) return NotFound();

                var client = GetClient(account);
        if (client is null) return NotFound();

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync())
            return NotFound();

        // Files are stored at <current prefix>/<optional new folder>/<file name>. The folder
        // structure exists purely because a real blob lives at that path; blob storage has no
        // standalone folders. The prefix and folder are validated once, before any file is
        // touched; a file whose own name is unsafe is skipped rather than failing the batch.
        if (!StorageBlobNames.IsValidPrefix(prefix?.Trim())) return NotFound();
        var subFolder = folder?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(subFolder) && !StorageBlobNames.IsValidPrefix(subFolder)) return NotFound();

        foreach (var file in files ?? [])
        {
            if (file.Length == 0) continue;

            var blobName = StorageBlobNames.ResolveUploadName(prefix, folder, file.FileName);
            if (blobName is null) continue;

            var blobClient = container.GetBlobClient(blobName);
            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
            });
        }

        return RedirectToContainer(account, containerName, prefix);
    }

    // ── #568 chunked upload ─────────────────────────────────────────────────────────────────
    // The browser sends each file in parts. Every part is one PUT with a raw body, staged as an
    // uncommitted block; a final POST commits the block list. No server state: the block ids
    // are derived from the upload id and the part index, so a request can only ever touch the
    // blocks of the upload it names, and the commit re-derives the same list. Bounds come
    // from configuration; the request ceiling is an attribute because Kestrel's is off.

    [HttpPut("admin/storage/{account}/{containerName}/upload/block")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(StorageBrowserOptions.ChunkRequestCeilingBytes)]
    public async Task<IActionResult> StageBlock(
        string account, string containerName,
        [FromQuery] string? prefix, [FromQuery] string? folder, [FromQuery] string? fileName,
        [FromQuery] Guid uploadId, [FromQuery] int index,
        CancellationToken cancellationToken)
    {
        var options = browserOptions.Value;
        if (IsProtected(containerName)) return NotFound();
        var client = GetClient(account);
        if (client is null) return NotFound();
        var blobName = StorageBlobNames.ResolveUploadName(prefix, folder, fileName);
        if (blobName is null) return NotFound();

        if (uploadId == Guid.Empty)
            return BadRequest(new { error = "The upload id is missing." });
        if (index < 0 || index >= options.MaxBlocks)
            return BadRequest(new { error = $"The part number must be between 0 and {options.MaxBlocks - 1}." });
        var length = Request.ContentLength;
        if (length is null || length <= 0 || length > options.ChunkBytes)
            return BadRequest(new { error = $"Each part must be between 1 and {options.ChunkBytes} bytes." });

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken)) return NotFound();

        // Bounded by the length check above, so at most ChunkBytes per in-flight request. Copied
        // rather than streamed because the SDK cannot rewind a request body to retry a part.
        using var buffer = new MemoryStream((int)length.Value);
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != length.Value)
            return BadRequest(new { error = "The part was shorter than its declared length." });
        buffer.Position = 0;

        await StorageChunkedUpload.StageAsync(container.GetBlockBlobClient(blobName), uploadId, index, buffer, cancellationToken);

        if (index == 0)
        {
            logger.LogInformation(
                "Storage browser chunked upload started {UploadId} to {Account}/{Container}/{Blob}",
                uploadId, account, containerName, blobName);
        }

        return NoContent();
    }

    // Kestrel's limit is off app-wide; the commit form is a handful of short fields.
    [HttpPost("admin/storage/{account}/{containerName}/upload/commit")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> CommitUpload(
        string account, string containerName,
        [FromForm] string? prefix, [FromForm] string? folder, [FromForm] string? fileName,
        [FromForm] Guid uploadId, [FromForm] int blockCount, [FromForm] string? contentType,
        CancellationToken cancellationToken)
    {
        var options = browserOptions.Value;
        if (IsProtected(containerName)) return NotFound();
        var client = GetClient(account);
        if (client is null) return NotFound();
        var blobName = StorageBlobNames.ResolveUploadName(prefix, folder, fileName);
        if (blobName is null) return NotFound();

        if (uploadId == Guid.Empty)
            return BadRequest(new { error = "The upload id is missing." });
        if (blockCount < 1 || blockCount > options.MaxBlocks)
            return BadRequest(new { error = $"The part count must be between 1 and {options.MaxBlocks}." });

        var container = client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken)) return NotFound();

        var outcome = await StorageChunkedUpload.CommitAsync(
            container.GetBlockBlobClient(blobName), uploadId, blockCount, options.MaxUploadBytes,
            SafeContentType(contentType), cancellationToken);

        switch (outcome.Status)
        {
            case ChunkedCommitStatus.Committed:
                logger.LogInformation(
                    "Storage browser chunked upload committed {UploadId} to {Account}/{Container}/{Blob}: {Bytes} bytes in {Blocks} parts",
                    uploadId, account, containerName, blobName, outcome.Bytes, blockCount);
                return Ok(new { name = blobName, bytes = outcome.Bytes });
            case ChunkedCommitStatus.TooLarge:
                logger.LogWarning(
                    "Storage browser chunked upload refused {UploadId} to {Account}/{Container}/{Blob}: {Bytes} bytes exceeds {MaxBytes}",
                    uploadId, account, containerName, blobName, outcome.Bytes, options.MaxUploadBytes);
                return BadRequest(new { error = $"The selected file must be smaller than {StorageBrowserOptions.FormatSize(options.MaxUploadBytes)}." });
            default:
                logger.LogWarning(
                    "Storage browser chunked upload incomplete {UploadId} to {Account}/{Container}/{Blob}: {Blocks} parts expected",
                    uploadId, account, containerName, blobName, blockCount);
                return Conflict(new { error = "The upload did not complete. Upload the file again." });
        }
    }

    // A media type for the blob's Content-Type header, or null for octet-stream. Only the simple
    // type/subtype shape is accepted, so request input cannot smuggle parameters or newlines.
    private static readonly Regex MediaType = new(@"^[A-Za-z0-9!#$&^_.+-]{1,64}/[A-Za-z0-9!#$&^_.+-]{1,64}$", RegexOptions.Compiled);

    private static string? SafeContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && MediaType.IsMatch(contentType.Trim()) ? contentType.Trim() : null;

    private IActionResult RedirectToContainer(string account, string containerName, string? prefix)
    {
        var url = $"/admin/storage/{account}/{containerName}";
        if (!string.IsNullOrWhiteSpace(prefix))
            url += $"?prefix={Uri.EscapeDataString(prefix)}";
        return Redirect(url);
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
