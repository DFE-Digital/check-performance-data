using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Media.Serving;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.Media;

/// <summary>
/// The media URL signing key: 32 random bytes, made by the first instance that needs it and kept
/// in the data-protection-keys container, encrypted with ASP.NET Data Protection. That container
/// already holds the shared Data Protection key ring and the storage browser hides it, and every
/// replica reads the same blob, so every replica signs with the same key. No secret has to be set
/// in any environment.
///
/// The blob is written create-only, so replicas that start together all keep the first key
/// written. If the key can no longer be decrypted (the key ring was lost), a new key replaces it;
/// that is safe because pages build media URLs when they render. The key is read once and then
/// held in memory. The read is synchronous because URLs are built while views render.
/// </summary>
public sealed class BlobMediaUrlSigningKey(
    BlobServiceClient service,
    IDataProtectionProvider protection,
    ILogger<BlobMediaUrlSigningKey> logger,
    string containerName = BlobMediaUrlSigningKey.ContainerName) : IMediaUrlSigningKey
{
    public const string ContainerName = "data-protection-keys";
    public const string BlobName = "media-url-signing-key";
    private const int KeyBytes = 32;
    private const int Attempts = 5;

    private readonly IDataProtector _protector = protection.CreateProtector("CPD.Media.UrlSigningKey.v1");
    private readonly Lock _lock = new();
    private byte[]? _key;

    public byte[] GetKey()
    {
        if (Volatile.Read(ref _key) is { } key) return key;
        lock (_lock)
        {
            _key ??= Load();
            return _key;
        }
    }

    private byte[] Load()
    {
        var container = service.GetBlobContainerClient(containerName);
        container.CreateIfNotExists();
        var blob = container.GetBlobClient(BlobName);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            BlobDownloadResult stored;
            try
            {
                stored = blob.DownloadContent().Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                if (TryWrite(blob, new BlobRequestConditions { IfNoneMatch = ETag.All }, out var created))
                {
                    logger.LogInformation("Created the media URL signing key");
                    return created;
                }
                continue;
            }

            if (TryUnprotect(stored.Content.ToArray(), out var key)) return key;

            logger.LogWarning("The media URL signing key cannot be decrypted; replacing it");
            if (TryWrite(blob, new BlobRequestConditions { IfMatch = stored.Details.ETag }, out var replaced))
                return replaced;
        }
        throw new InvalidOperationException("The media URL signing key could not be read or created.");
    }

    // Writes a new key under the given condition. False when another instance wrote first.
    private bool TryWrite(BlobClient blob, BlobRequestConditions conditions, out byte[] key)
    {
        key = RandomNumberGenerator.GetBytes(KeyBytes);
        try
        {
            blob.Upload(BinaryData.FromBytes(_protector.Protect(key)), new BlobUploadOptions { Conditions = conditions });
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    private bool TryUnprotect(byte[] stored, out byte[] key)
    {
        try
        {
            key = _protector.Unprotect(stored);
            return key.Length == KeyBytes;
        }
        catch (CryptographicException)
        {
            key = [];
            return false;
        }
    }
}
