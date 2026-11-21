namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The "Delete unused images" confirmation page: the unused images that match the library search,
/// at most one run of them. TotalCount is every unused image that matches, so the page can say
/// when a run takes only the first ones.
/// </summary>
public sealed class MediaDeleteUnusedViewModel
{
    public required IReadOnlyList<MediaLibraryItemViewModel> Items { get; init; }

    public required int TotalCount { get; init; }

    public string? Search { get; init; }
}
