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
}
