using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The media library page: one page of images with the search, filters, sort and view that chose
/// it, and, after a plain form upload, what happened to each file.
/// </summary>
public sealed class MediaLibraryIndexViewModel
{
    public required MediaAssetPage Page { get; init; }

    public required IReadOnlyList<MediaLibraryItemViewModel> Items { get; init; }

    public string? Search { get; init; }

    public bool MissingAlt { get; init; }

    public bool Unused { get; init; }

    /// <summary>"newest" or "name", as the sort control and page links send it.</summary>
    public string Sort { get; init; } = "newest";

    /// <summary>One of <see cref="MediaLibraryView"/>: grid, details or large.</summary>
    public string View { get; init; } = MediaLibraryView.Grid;

    /// <summary>The list with no search or filters, in the same view.</summary>
    public string ClearFiltersUrl => View == MediaLibraryView.Grid ? "/admin/media" : "/admin/media?view=" + View;

    public IReadOnlyList<MediaUploadItemViewModel> UploadResults { get; init; } = [];

    /// <summary>The error for the file control itself, for example when no file was chosen.</summary>
    public string? UploadError { get; init; }

    public bool IsFiltered => !string.IsNullOrEmpty(Search) || MissingAlt || Unused;

    /// <summary>The query string for one page of this same list. Page is 1-based.</summary>
    public string PageUrl(int page)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(Search)) query.Add("search=" + Uri.EscapeDataString(Search));
        if (MissingAlt) query.Add("missingAlt=true");
        if (Unused) query.Add("unused=true");
        if (Sort != "newest") query.Add("sort=" + Uri.EscapeDataString(Sort));
        if (View != MediaLibraryView.Grid) query.Add("view=" + View);
        if (page > 1) query.Add("page=" + page);
        return "/admin/media" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
    }
}
