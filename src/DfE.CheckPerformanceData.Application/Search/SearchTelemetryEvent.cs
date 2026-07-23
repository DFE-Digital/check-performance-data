namespace DfE.CheckPerformanceData.Application.Search;

// Immutable snapshot of a completed search request: summary metrics, the ranked hits kept
// after all filters ran, and the per-row exclusion breadcrumbs for rows the tsquery matched
// but a filter dropped. Positional record so property order is the assembly contract — the
// downstream DB sink maps it 1:N to a summary row + per-hit rows without a reshape step.
//
// Latency fields:
//   LatencyMsTotal — end-to-end elapsed for the whole request (always populated).
//   LatencyMsPages — elapsed inside the PageNode search; null when the page corpus was
//                    skipped (e.g. scope filter routed the request to blocks only).
//   LatencyMsBlocks — elapsed inside the ContentBlock search; null under the mirror-image
//                     condition.
public sealed record SearchTelemetryEvent(
    Guid SearchId,
    DateTime UtcTimestamp,
    string QueryRaw,
    string QueryNormalised,
    string? Scope,
    long LatencyMsTotal,
    long? LatencyMsPages,
    long? LatencyMsBlocks,
    IReadOnlyList<SearchHitEvent> Hits,
    IReadOnlyList<FilterExclusion> FilterExclusions);

// One entry per hit kept after filtering. Corpus is a discriminator ("page" or "block")
// because the two corpora share the event surface but have different natural row keys and
// per-field weightings.
//
// RankTotal is the combined ts_rank the ordering step already uses; per-field ranks are
// the additive components projected alongside so downstream analysis can answer "why did
// this rank above that". Any per-field rank is nullable because the field may not apply
// to the corpus (e.g. RankValue is only meaningful for content blocks; RankBody is only
// meaningful for pages) — a null means "not applicable in this row's corpus", NOT "unknown".
public sealed record SearchHitEvent(
    string Corpus,
    string RowId,
    string Url,
    string Title,
    float RankTotal,
    float? RankKeywords,
    float? RankTitle,
    float? RankSubtitle,
    float? RankBody,
    float? RankValue);

// One entry per row the tsquery matched but a filter dropped. Reused to produce the
// "why didn't page X appear for query Y?" answer from log search alone.
//
// Kind is the domain slug for the specific silent-filter rule that dropped the row. The
// slugs are string-valued (not an enum) so new filter kinds added in later phases can
// flow through the seam without a downstream code change. The current slug set is:
//   admin-path                          — content block last seen under an admin route
//   e2e-key                             — content-block key reserved for E2E fixtures
//   guidance-ks4-2026-nav-key           — nav-only fixture block
//   contentblock-appearinsearch-false   — editor toggled block out of search
//   pagenode-appearinsearch-false       — editor toggled page out of search
//   draft-page                          — page has no live version
//   unpublished-target                  — content-block resolver landed on a page without
//                                         a live version
//
// RowKey is the natural identifier for the excluded row: ContentBlock.Key for blocks,
// PageNode.Path for pages. That matches the domain slugs a human diagnoses against.
public sealed record FilterExclusion(
    string Corpus,
    string Kind,
    string RowKey);
