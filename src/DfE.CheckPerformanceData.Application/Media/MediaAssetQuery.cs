namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Filters for the library list. Search matches the file name, alt text, caption, credit or a tag
/// (case-insensitive and literal: wildcard characters match themselves). Tag matches one tag
/// exactly, ignoring case. MissingAltText keeps images with no default alt text; UnusedOnly keeps
/// images no page version uses. Page is 1-based.
/// </summary>
public sealed record MediaAssetQuery(
    string? Search = null,
    string? Tag = null,
    bool MissingAltText = false,
    bool UnusedOnly = false,
    int Page = 1,
    int PageSize = 20);
