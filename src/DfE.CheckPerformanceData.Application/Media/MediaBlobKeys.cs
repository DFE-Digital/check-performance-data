namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Blob names in the cms-media container. Originals sit under their image and version; renditions
/// under the version they were made from and a hash of the rendition parameters, so every
/// rendition of one version can be listed or removed by prefix.
/// </summary>
public static class MediaBlobKeys
{
    public const string ContainerName = "cms-media";

    public static string Original(Guid assetId, int version) =>
        $"originals/{assetId:D}/{version}";

    public static string Rendition(Guid assetId, int version, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        if (!hash.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A rendition hash must be letters and digits only.", nameof(hash));
        return $"{RenditionsPrefix(assetId, version)}{hash}";
    }

    public static string RenditionsPrefix(Guid assetId, int version) =>
        $"renditions/{assetId:D}/{version}/";

    /// <summary>The prefixes that hold every stored file of one image: its originals and its renditions.</summary>
    public static IReadOnlyList<string> AssetPrefixes(Guid assetId) =>
        [$"originals/{assetId:D}/", $"renditions/{assetId:D}/"];

    /// <summary>The prefix that holds every rendition of every image.</summary>
    public const string AllRenditionsPrefix = "renditions/";

    /// <summary>The image a rendition key belongs to. False for any other key.</summary>
    public static bool TryGetRenditionAsset(string key, out Guid assetId)
    {
        assetId = Guid.Empty;
        if (!key.StartsWith(AllRenditionsPrefix, StringComparison.Ordinal)) return false;
        var rest = key[AllRenditionsPrefix.Length..];
        var slash = rest.IndexOf('/');
        return slash > 0 && Guid.TryParseExact(rest[..slash], "D", out assetId);
    }
}
