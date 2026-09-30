using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Holds the "Not on roll" FE college list (AB#304119) in memory and keeps it in step with
/// <c>not-on-roll-colleges.json</c> in the rules-config container. Registered as a singleton:
/// journey conditions read <see cref="Current"/> synchronously on every render, so it must never
/// touch storage.
///
/// The bundled file in the release image is the source of truth. <see cref="SeedAsync"/> replaces
/// the blob when it differs, so a change to the list goes live by deploying it. Terraform
/// provisions the container empty and the storage account has no public network access, so the
/// app writes the blob itself, as it does for the qualification reference.
///
/// Every storage failure is logged and swallowed. The list the store already holds stays in use,
/// so a storage blip never hides "Not on roll" from a college that saw it a minute ago.
/// </summary>
public sealed class NotOnRollCollegeListStore : INotOnRollCollegeListProvider
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobRulesProviderOptions _options;
    private readonly ILogger<NotOnRollCollegeListStore> _logger;

    private NotOnRollCollegeList _current = NotOnRollCollegeList.Empty;

    public NotOnRollCollegeListStore(
        BlobServiceClient blobServiceClient,
        IOptions<BlobRulesProviderOptions> options,
        ILogger<NotOnRollCollegeListStore> logger)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
        _logger = logger;
    }

    public NotOnRollCollegeList Current => Volatile.Read(ref _current);

    /// <summary>
    /// Uses the bundled list until storage has been read, so a college is not refused the reason
    /// because the first read failed. Malformed JSON throws: the bundled file ships in the image,
    /// so a broken copy is a build defect and should fail loudly.
    /// </summary>
    public void UseBundled(string bundledJson) => Install(NotOnRollCollegeList.Parse(bundledJson));

    /// <summary>
    /// Writes the bundled JSON to the blob unless the blob already holds exactly that text. The
    /// write is conditional on what was read (If-Match on its ETag, or If-None-Match=* when there
    /// was no blob), so of two pods starting together only one writes.
    /// </summary>
    public async Task SeedAsync(string bundledJson, CancellationToken ct = default)
    {
        try
        {
            var container = Container();
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient(NotOnRollCollegeList.BlobName);

            var conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
            try
            {
                var existing = await blob.DownloadContentAsync(ct);
                if (existing.Value.Content.ToString() == bundledJson)
                {
                    _logger.LogInformation(
                        "Not on roll college list '{Blob}' already matches the bundled copy; skipping seed.",
                        NotOnRollCollegeList.BlobName);
                    return;
                }

                conditions = new BlobRequestConditions { IfMatch = existing.Value.Details.ETag };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Fresh environment: create it.
            }

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundledJson));
            await blob.UploadAsync(stream, new BlobUploadOptions { Conditions = conditions }, ct);
            _logger.LogInformation(
                "Seeded not on roll college list '{Blob}' into container '{Container}'.",
                NotOnRollCollegeList.BlobName, _options.RulesBlobContainer);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // Another pod wrote it between our read and our write.
            _logger.LogInformation(
                "Not on roll college list '{Blob}' was written by another instance; skipping seed.",
                NotOnRollCollegeList.BlobName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed not on roll college list '{Blob}'.", NotOnRollCollegeList.BlobName);
        }
    }

    /// <summary>
    /// Reads the blob and installs the list it holds. A missing, unreadable or malformed blob
    /// leaves the current list in place.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await Container()
                .GetBlobClient(NotOnRollCollegeList.BlobName)
                .DownloadContentAsync(ct);
            Install(NotOnRollCollegeList.Parse(response.Value.Content.ToString()));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(
                "Not on roll college list '{Blob}' is not in container '{Container}'; keeping the {Count} " +
                "colleges already loaded.", NotOnRollCollegeList.BlobName, _options.RulesBlobContainer, Current.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read not on roll college list '{Blob}'; keeping the {Count} colleges already loaded.",
                NotOnRollCollegeList.BlobName, Current.Count);
        }
    }

    private void Install(NotOnRollCollegeList list) => Volatile.Write(ref _current, list);

    private BlobContainerClient Container() =>
        _blobServiceClient.GetBlobContainerClient(_options.RulesBlobContainer);
}
