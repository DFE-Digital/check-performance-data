namespace DfE.CheckPerformanceData.Application.Analytics;

// What a typeahead reports once the person has settled on a query.
//
// Deliberately a different record from SearchTelemetryEvent. That one describes a search the
// server ran and carries per-field rank breakdowns and filter breadcrumbs it owns; this one
// describes what a person did in a menu, and the only authority on what was actually shown to
// them, and on whether they took any of it, is the browser. Keeping the two apart stops either
// from growing fields that are meaningless on the other.
public sealed record InstantSearchTelemetryEvent(
    Guid SearchId,
    DateTime UtcTimestamp,
    string QueryRaw,
    string QueryNormalised,
    string? Scope,
    // SearchSurfaces.Instant or SearchSurfaces.InstantPage.
    string Surface,
    // The page the widget sat on, for an on-page search; null otherwise.
    string? HostPath,
    // Time from the query being issued to the menu rendering, measured in the browser.
    int LatencyMs,
    IReadOnlyList<InstantSearchShownHit> Shown,
    // Null when the menu was shown and nothing was taken from it.
    string? SelectedKey,
    int? SelectedPosition);

// One row as it appeared in the menu. Position is one-indexed and is the order the person saw,
// which is the only ordering that means anything when asking why they picked the third one.
public sealed record InstantSearchShownHit(
    int Position,
    // "page" for a document, "section" for a heading on the current page.
    string Kind,
    // Canonical URL for a page, "#anchor" for a section.
    string Key,
    string Label);
