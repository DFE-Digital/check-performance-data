using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SiteSearchViewModel
{
    public required string Query { get; init; }
    public required string? Scope { get; init; }
    public required SearchInvalidReason? InvalidReason { get; init; }
    public required IReadOnlyList<PageSearchHitDto> PageHits { get; init; }
    public required IReadOnlyList<ContentBlockSearchResultDto> ContentBlockHits { get; init; }
    public required bool IncludePages { get; init; }
    public required bool IncludeContentBlocks { get; init; }
    public int TotalHits => PageHits.Count + ContentBlockHits.Count;
}
