namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Makes the public URLs of library images, at the time a page is rendered. Pages store only an
/// image id; the caller picks the version (the latest for live and draft pages, the recorded one
/// for a historic page version) and passes it here. URLs are root-relative:
/// <c>/media/{id}/{version}/{slug}.{extension}</c>, with signed settings for a rendition.
/// </summary>
public interface IMediaUrlBuilder
{
    /// <summary>The stored file of <paramref name="version"/>.</summary>
    string Original(MediaAssetDto asset, MediaAssetVersionDto version);

    /// <summary>
    /// A signed WebP copy at one of <see cref="MediaRenditionQuery.AllowedWidths"/>, optionally
    /// cropped and cut to an aspect ratio. An SVG, an animation or an image of unknown size is
    /// never resized, so for those this is <see cref="Original"/>. Throws for a width outside the
    /// set, a crop or focal point outside the image, or a focal point without an aspect ratio.
    /// </summary>
    string Rendition(MediaAssetDto asset, MediaAssetVersionDto version, int width,
        MediaCrop? crop = null, MediaAspectRatio? aspect = null, MediaFocalPoint? focal = null);

    /// <summary>
    /// A srcset value: one signed copy for each fixed width up to the width of the area shown,
    /// each with the width it really has. Empty when the image is never resized.
    /// </summary>
    string SrcSet(MediaAssetDto asset, MediaAssetVersionDto version,
        MediaCrop? crop = null, MediaAspectRatio? aspect = null, MediaFocalPoint? focal = null);
}
