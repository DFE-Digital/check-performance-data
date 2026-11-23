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
    /// <summary>The number of pages and content blocks that use the image.</summary>
    public int Uses { get; init; }

    /// <summary>Who uploaded the current file, and when.</summary>
    public string? UploadedBy { get; init; }

    public DateTime UploadedAtUtc { get; init; }

    /// <summary>A signed copy for the Large view, or the original when the file is never resized.</summary>
    public string LargeUrl { get; init; } = "";

    public const int ThumbnailWidth = 320;

    public const int LargeWidth = 960;

    public bool MissingAltText => string.IsNullOrWhiteSpace(AltText);

    /// <summary>The format's short name, for example "PNG". An unknown type shows as stored.</summary>
    public string TypeName => ContentType switch
    {
        "image/png" => "PNG",
        "image/jpeg" => "JPEG",
        "image/gif" => "GIF",
        "image/webp" => "WebP",
        "image/svg+xml" => "SVG",
        _ => ContentType,
    };

    public static string DetailUrlFor(Guid id) => $"/admin/media/{id:D}";

    /// <summary>"640 × 480 pixels", or null when the file has no intrinsic size.</summary>
    public static string? DimensionsOf(MediaAssetVersionDto version) =>
        version.Width is { } w && version.Height is { } h ? $"{w} × {h} pixels" : null;

    public static MediaLibraryItemViewModel From(MediaAssetDto asset, IMediaUrlBuilder urls, int uses = 0)
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
            asset.UpdatedAtUtc)
        {
            Uses = uses,
            UploadedBy = latest.UploadedBy,
            UploadedAtUtc = latest.UploadedAtUtc,
            LargeUrl = urls.Rendition(asset, latest, LargeWidth),
        };
    }
}
