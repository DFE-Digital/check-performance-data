namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Filters for the library list. Search matches the file name, alt text, caption, credit or a tag
/// (case-insensitive and literal: wildcard characters match themselves). Tag matches one tag
/// exactly, ignoring case. MissingAltText keeps images with no default alt text; UnusedOnly keeps
/// images no page version uses. Sort sets the order. Page is 1-based.
/// FolderId and IncludeSubfolders choose the folder: with IncludeSubfolders, the folder and every
/// folder inside it; without, that folder only. A null FolderId is the top of the library, so the
/// defaults (null, true) list the whole library, and (null, false) lists only the images that are
/// in no folder.
/// </summary>
public sealed record MediaAssetQuery(
    string? Search = null,
    string? Tag = null,
    bool MissingAltText = false,
    bool UnusedOnly = false,
    int Page = 1,
    int PageSize = 20,
    MediaAssetSort Sort = MediaAssetSort.Newest,
    Guid? FolderId = null,
    bool IncludeSubfolders = true);
