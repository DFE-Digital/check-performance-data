using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// One library image as the media picker and the image editor use it, sent as JSON. The thumbnail
/// is a signed 320 pixel copy. <see cref="EditUrl"/> is a signed, uncropped copy that is large
/// enough to crop on and to set a focal point on. <see cref="CanCrop"/> is false for an image that
/// is always served as stored (SVG, GIF, an animation, or an image of unknown size), because a crop
/// and a focal point have no effect on it. <see cref="Duplicate"/> is true when an upload matched
/// an image that is already in the library.
/// </summary>
public sealed record MediaPickerItem(
    Guid Id,
    string FileName,
    string Alt,
    int? Width,
    int? Height,
    string ThumbUrl,
    string EditUrl,
    bool IsSvg,
    bool IsAnimated,
    bool CanCrop,
    bool Duplicate = false)
{
    public const int CropSurfaceWidth = 960;

    public static MediaPickerItem From(MediaAssetDto asset, IMediaUrlBuilder urls, bool duplicate = false)
    {
        var latest = asset.Latest;
        return new MediaPickerItem(
            asset.Id,
            asset.FileName,
            asset.DefaultAltText ?? "",
            latest.Width,
            latest.Height,
            urls.Rendition(asset, latest, MediaLibraryItemViewModel.ThumbnailWidth),
            urls.Rendition(asset, latest, CropSurfaceWidth),
            latest.ContentType == "image/svg+xml",
            latest.IsAnimated,
            MediaUrlBuilder.IsResizable(latest),
            duplicate);
    }
}
