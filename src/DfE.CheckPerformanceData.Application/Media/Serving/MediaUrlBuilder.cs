using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Builds media URLs. The slug and extension are for people and for saved file names; the media
/// endpoint serves by id and version and ignores them. A rendition always ends .webp, because
/// that is what it serves.
/// </summary>
public sealed class MediaUrlBuilder(MediaUrlSigner signer) : IMediaUrlBuilder
{
    private const string RenditionExtension = "webp";

    public string Original(MediaAssetDto asset, MediaAssetVersionDto version) =>
        Path(asset, version, Extension(version.ContentType));

    public string Rendition(MediaAssetDto asset, MediaAssetVersionDto version, int width,
        MediaCrop? crop = null, MediaAspectRatio? aspect = null, MediaFocalPoint? focal = null)
    {
        var rendition = MediaRenditionQuery.Normalise(width, crop, aspect, focal);
        return IsResizable(version) ? Signed(asset, version, rendition) : Original(asset, version);
    }

    public string SrcSet(MediaAssetDto asset, MediaAssetVersionDto version,
        MediaCrop? crop = null, MediaAspectRatio? aspect = null, MediaFocalPoint? focal = null)
    {
        var shape = MediaRenditionQuery.Normalise(null, crop, aspect, focal);
        if (!IsResizable(version)) return "";

        var entries = new List<string>();
        foreach (var width in MediaRenditionQuery.AllowedWidths)
        {
            var rendition = shape with { Width = width };
            var layout = MediaRenditionGeometry.Compute(version.Width!.Value, version.Height!.Value, rendition);
            entries.Add($"{Signed(asset, version, rendition)} {layout.Width}w");
            // A copy at the area's own width is the largest there is; wider ones would be the same.
            if (layout.Width == layout.Source.Width) break;
        }
        return string.Join(", ", entries);
    }

    /// <summary>
    /// True for a static PNG, JPEG or WebP of known size. An SVG scales by itself, and Skia cannot
    /// write an animation, so those are always served as stored.
    /// </summary>
    public static bool IsResizable(MediaAssetVersionDto version) =>
        !version.IsAnimated
        && version.Width is > 0 && version.Height is > 0
        && version.ContentType is "image/png" or "image/jpeg" or "image/webp";

    private string Signed(MediaAssetDto asset, MediaAssetVersionDto version, MediaRendition rendition) =>
        $"{Path(asset, version, RenditionExtension)}?{MediaRenditionQuery.WithSignature(rendition, signer.Sign(version.AssetId, version.Version, rendition))}";

    private static string Path(MediaAssetDto asset, MediaAssetVersionDto version, string extension)
    {
        if (asset.Id != version.AssetId)
            throw new ArgumentException("The version belongs to another image.", nameof(version));
        return $"/media/{version.AssetId:D}/{version.Version}/{Uri.EscapeDataString(asset.Slug)}.{extension}";
    }

    private static string Extension(string contentType)
    {
        foreach (var format in Enum.GetValues<MediaFormat>())
        {
            if (MediaFormats.ContentType(format) == contentType) return MediaFormats.Extension(format);
        }
        throw new ArgumentException($"No media format has the content type {contentType}.", nameof(contentType));
    }
}
