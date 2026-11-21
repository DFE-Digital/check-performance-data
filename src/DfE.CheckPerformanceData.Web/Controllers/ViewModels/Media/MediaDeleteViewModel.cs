using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The delete page of a library image. An image that any page version or content block uses cannot be
/// deleted, so the page lists them instead of offering the delete button. This includes older page
/// versions: they must still show the file they were saved with.
/// </summary>
public sealed class MediaDeleteViewModel
{
    public required MediaLibraryItemViewModel Item { get; init; }

    public string FileName => Item.FileName;

    public required IReadOnlyList<MediaUsagePage> UsagePages { get; init; }

    public IReadOnlyList<MediaUsageBlock> UsageBlocks { get; init; } = [];

    public MediaUsageSummary Usage => new(UsagePages, UsageBlocks);

    public bool CanDelete => !Usage.IsUsed;
}
