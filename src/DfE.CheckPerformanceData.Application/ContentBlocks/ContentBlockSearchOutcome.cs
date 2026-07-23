using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.ContentBlocks;

// Post-widen return shape from IContentBlockSearchService.SearchAsync — bundles the kept
// hits (what the user sees) with the per-row exclusion breadcrumbs (what telemetry needs).
// The next wave of work replaces the transitional service-level defensive filter with a
// direct read of Hits, then propagates Exclusions into the outer SiteSearchService's
// telemetry event. This plan publishes the record so the follow-on wave can refactor to
// consume it without an additional shape churn.
public sealed record ContentBlockSearchOutcome(
    IReadOnlyList<ContentBlockSearchResultDto> Hits,
    IReadOnlyList<FilterExclusion> Exclusions);
