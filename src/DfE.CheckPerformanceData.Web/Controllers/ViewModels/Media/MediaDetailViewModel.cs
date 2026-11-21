using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// One library image's page: a large preview, the facts about its current file, its public link,
/// the "Image details" form, the replace form, its version history and where pages use it.
/// </summary>
public sealed class MediaDetailViewModel
{
    public required MediaLibraryItemViewModel Item { get; init; }

    public string FileName => Item.FileName;

    /// <summary>A signed 960 pixel copy, or the stored file for an SVG or an animation.</summary>
    public required string PreviewUrl { get; init; }

    /// <summary>The full address of the current original, for the "Copy link" button.</summary>
    public required string PublicUrl { get; init; }

    public string? UploadedBy { get; init; }

    public DateTime UploadedAtUtc { get; init; }

    public required IReadOnlyList<MediaVersionItemViewModel> Versions { get; init; }

    public required IReadOnlyList<MediaUsagePage> UsagePages { get; init; }

    public IReadOnlyList<MediaUsageBlock> UsageBlocks { get; init; } = [];

    public MediaUsageSummary Usage => new(UsagePages, UsageBlocks);

    public required MediaMetadataForm Metadata { get; init; }

    public string DeleteUrl => $"{Item.DetailUrl}/delete";
}
