using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// One library image as the admin pages show it. The thumbnail is a signed 320 pixel copy; an SVG,
/// an animation or an image of unknown size gets its original file instead, which the page shows
/// in a fixed box (<see cref="IsShownAsStored"/>).
/// </summary>
public sealed record MediaLibraryItemViewModel(
    Guid Id,
    string FileName,
    string ThumbnailUrl,
    string OriginalUrl,
    string DetailUrl,
    string? Dimensions,
    string Size,
    string ContentType,
    string? AltText,
    int Version,
    bool IsShownAsStored,
    DateTime UpdatedAtUtc)
{
    public const int ThumbnailWidth = 320;

    public bool MissingAltText => string.IsNullOrWhiteSpace(AltText);

    public static string DetailUrlFor(Guid id) => $"/admin/media/{id:D}";

    /// <summary>"640 × 480 pixels", or null when the file has no intrinsic size.</summary>
    public static string? DimensionsOf(MediaAssetVersionDto version) =>
        version.Width is { } w && version.Height is { } h ? $"{w} × {h} pixels" : null;

    public static MediaLibraryItemViewModel From(MediaAssetDto asset, IMediaUrlBuilder urls)
    {
        var latest = asset.Latest;
        return new MediaLibraryItemViewModel(
            asset.Id,
            asset.FileName,
            urls.Rendition(asset, latest, ThumbnailWidth),
            urls.Original(asset, latest),
            DetailUrlFor(asset.Id),
            DimensionsOf(latest),
            MediaFileSize.Format(latest.SizeBytes),
            latest.ContentType,
            asset.DefaultAltText,
            asset.CurrentVersion,
            !MediaUrlBuilder.IsResizable(latest),
            asset.UpdatedAtUtc);
    }
}
