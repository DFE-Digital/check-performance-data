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

// One entry per canonical URL kept after filtering — the same set of rows the /search view
// renders. Two blocks on the same page URL fold into ONE SearchHitEvent (with
// BlockContributorCount = 2); a page + block on the same URL fold into ONE SearchHitEvent
// (with PageContributed = true AND BlockContributorCount = 1). The URL is the identity;
// there is no separate row-id field.
//
// RankTotal is the canonical row's aggregate rank (MAX of contributor raw ts_ranks — the
// ordering key the /search view uses). Per-field ranks are the WINNING contributor's
// per-field breakdown — the contributor whose snippet the canonicaliser picked. Because
// the winner may be a page (Value N/A) or a block (Title/Subtitle/Body N/A), every
// per-field rank is nullable — a null means "not applicable in the winning contributor's
// corpus", NOT "unknown".
//
// PageContributed is true when a page-corpus row contributed to this canonical URL
// (max 1 page contributor per URL, so a bool captures it). BlockContributorCount is the
// count of block-corpus contributors folded in (0..N). ContributingBlockKeys lists those
// block keys in input order — an empty list when the row is pure-page-sourced.
public sealed record SearchHitEvent(
    string Url,
    string Title,
    float RankTotal,
    bool PageContributed,
    int BlockContributorCount,
    IReadOnlyList<string> ContributingBlockKeys,
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
