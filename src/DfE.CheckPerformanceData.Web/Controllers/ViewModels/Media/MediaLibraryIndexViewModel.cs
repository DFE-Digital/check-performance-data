using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Folders;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The media library page: one page of images in one folder, with the search, filters, sort and
/// view that chose it, and, after a plain form upload, what happened to each file. Every link and
/// form on the page keeps the folder and the "Include subfolders" choice through <see cref="Query"/>.
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

    /// <summary>Every folder of the library.</summary>
    public MediaFolderTree Folders { get; init; } = MediaFolderTree.Empty;

    /// <summary>The folder shown, or null for the top of the library.</summary>
    public MediaFolderDto? Folder { get; init; }

    /// <summary>
    /// True (the default) lists the folder and every folder inside it; false lists the folder only.
    /// At the top of the library, false lists only the images that are in no folder.
    /// </summary>
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>The name typed into "Create a folder", and why it was refused.</summary>
    public string? NewFolderName { get; set; }

    public string? NewFolderError { get; set; }

    /// <summary>Why the last "Move selected images" was refused.</summary>
    public string? MoveError { get; set; }

    public IReadOnlyList<MediaFolderDto> Subfolders => Folders.ChildrenOf(Folder?.Id);

    /// <summary>The folders above the current folder, and the folder itself, from the top down.</summary>
    public IReadOnlyList<MediaFolderDto> FolderPath => Folders.PathOf(Folder?.Id);

    public string Heading => Folder?.Name ?? "Media library";

    /// <summary>Every folder, in tree order and named by its path, for "Move selected images".</summary>
    public IReadOnlyList<MediaFolderOption> FolderOptions =>
        Folders.Flatten().Select(i => new MediaFolderOption(i.Folder.Id, Folders.PathText(i.Folder.Id))).ToList();

    /// <summary>The list with no search or filters, in the same folder and view.</summary>
    public string ClearFiltersUrl => Url(Query(keepFilters: false));

    public IReadOnlyList<MediaUploadItemViewModel> UploadResults { get; set; } = [];

    /// <summary>The error for the file control itself, for example when no file was chosen.</summary>
    public string? UploadError { get; set; }

    /// <summary>"Include subfolders" off narrows the list, so it counts as a filter. The folder does not.</summary>
    public bool IsFiltered => !string.IsNullOrEmpty(Search) || MissingAlt || Unused || !IncludeSubfolders;

    /// <summary>The upload form's address. New images go into the folder shown.</summary>
    public string UploadUrl => Folder is null ? "/admin/media/upload" : $"/admin/media/upload?folder={Folder.Id:D}";

    /// <summary>"Delete unused images" for the images this list can show: the same folder and search.</summary>
    public string DeleteUnusedUrl
    {
        get
        {
            var query = FolderQuery();
            if (!string.IsNullOrEmpty(Search)) query.Add("search=" + Uri.EscapeDataString(Search));
            return "/admin/media/delete-unused" + Join(query);
        }
    }

    /// <summary>The address of a folder in the same view.</summary>
    public string FolderUrl(Guid? folderId)
    {
        var query = new List<string>();
        if (folderId is Guid id) query.Add($"folder={id:D}");
        if (View != MediaLibraryView.Grid) query.Add("view=" + View);
        return Url(query);
    }

    /// <summary>The query string for one page of this same list. Page is 1-based.</summary>
    public string PageUrl(int page)
    {
        var query = Query();
        if (page > 1) query.Add("page=" + page);
        return Url(query);
    }

    /// <summary>
    /// The query parts that choose this list, in a fixed order: folder, subfolders, search,
    /// filters, sort and view. Defaults are left out, so the plain library is "/admin/media".
    /// </summary>
    public List<string> Query(bool keepFilters = true)
    {
        var query = keepFilters ? FolderQuery() : Folder is null ? [] : [$"folder={Folder.Id:D}"];
        if (keepFilters)
        {
            if (!string.IsNullOrEmpty(Search)) query.Add("search=" + Uri.EscapeDataString(Search));
            if (MissingAlt) query.Add("missingAlt=true");
            if (Unused) query.Add("unused=true");
            if (Sort != "newest") query.Add("sort=" + Uri.EscapeDataString(Sort));
        }
        if (View != MediaLibraryView.Grid) query.Add("view=" + View);
        return query;
    }

    private List<string> FolderQuery()
    {
        var query = new List<string>();
        if (Folder is not null) query.Add($"folder={Folder.Id:D}");
        if (!IncludeSubfolders) query.Add("subfolders=false");
        return query;
    }

    private static string Url(List<string> query) => "/admin/media" + Join(query);

    private static string Join(List<string> query) => query.Count == 0 ? "" : "?" + string.Join("&", query);
}
