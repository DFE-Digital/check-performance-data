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

    public SearchInvalidReason? InvalidReason { get; set; }

    // Populated from InvalidReason at controller level — Razor reads this verbatim.
    public List<string> ErrorMessages { get; set; } = [];

    // Stable DOM id for <govuk-input> so the error-summary anchor link resolves.
    public string InputId { get; set; } = "search-q";
}
