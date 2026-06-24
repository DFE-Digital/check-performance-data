using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SearchResultsViewModel
{
    public string CurrentQuery { get; set; } = string.Empty;
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }

    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;

    public List<WikiPageSearchResultDto> Results { get; set; } = [];

    // Content-block matches (guidance/service pages), shown alongside the wiki results.
    public List<ContentBlockSearchResultDto> ContentResults { get; set; } = [];

    public bool HasAnyResults => Results.Count > 0 || ContentResults.Count > 0;

    public SearchInvalidReason? InvalidReason { get; set; }

    // Populated from InvalidReason at controller level — Razor reads this verbatim.
    public List<string> ErrorMessages { get; set; } = [];

    // Stable DOM id for <govuk-input> so the error-summary anchor link resolves.
    public string InputId { get; set; } = "search-q";

    // Navigation sidebar content — mirrors HelpViewModel so the Search page can reuse
    // the wiki-layout + _WikiTree partial and feel consistent with /help.
    public List<WikiPageTreeNodeDto> NavigationTree { get; set; } = [];
}
