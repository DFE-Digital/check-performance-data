using DfE.CheckPerformanceData.Application.Media.Folders;

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

    /// <summary>The folder the list came from, or null for the top of the library.</summary>
    public MediaFolderDto? Folder { get; init; }

    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>The library list of unused images this page came from.</summary>
    public string BackUrl { get; init; } = "/admin/media?unused=true";
}
