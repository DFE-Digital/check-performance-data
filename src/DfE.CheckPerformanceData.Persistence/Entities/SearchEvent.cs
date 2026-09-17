using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformance.Persistence.Entities;

// An append-only record of a search request against the site-search corpus. Keyed by an
// opaque per-session identifier so support can find a complainant's history from the
// session id they quote; the row deliberately carries no other person-linkable field
// (no user sub, no client IP hash, no user-agent family, no roles) so a sink DB leak
// reveals nothing that ties a row to a human. Child hit rows live in SearchEventResult.
// ResultsTotal and ZeroResults are database-computed columns; the sink writer sets only
// ResultsPages + ResultsBlocks and Postgres derives the rest.
public sealed class SearchEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? QueryRaw { get; set; }
    public string? QueryNormalised { get; set; }
    public string? Scope { get; set; }
    public int ResultsPages { get; set; }
    public int ResultsBlocks { get; set; }

    // Sections of a single page offered by an on-page instant search. A third result kind
    // rather than a reuse of ResultsPages, because a section is not a document: counting
    // them as pages would inflate every per-page figure on the dashboard. Folded into the
    // computed ResultsTotal/ZeroResults so an on-page search that showed three sections is
    // not filed as a zero-result search.
    public int ResultsSections { get; set; }

    public int ResultsTotal { get; set; }
    public bool ZeroResults { get; set; }
    public int LatencyMs { get; set; }

    // Which search surface produced the row: "site" for a submitted search at /search or a
    // results widget, "instant" for a typeahead over the whole site or a section of it, and
    // "instant-page" for a typeahead over the sections of the page the widget sits on.
    // Existing rows migrate to "site", which is what they all were.
    public string Surface { get; set; } = SearchSurfaces.Site;

    // The page the widget was sitting on, for an on-page search. Null for every other
    // surface — a site search has no host page, it IS the search.
    public string? HostPath { get; set; }

    // What the person chose from the menu, and where it sat in the list. Null means the
    // menu was shown and nothing was taken from it: they either found their answer in the
    // list without clicking, or none of it was any good and they typed something else.
    // That second case is the signal an instant search cannot get any other way, so it is
    // recorded as deliberately as a selection is.
    public string? SelectedKey { get; set; }
    public int? SelectedPosition { get; set; }

    // True when the row was written by the sample-data seeder (dev-only Test-data admin
    // surface); false for every event captured from a real user request. Existing rows
    // default to false so the marker's write-once invariant survives the initial
    // migration without a data backfill. Admin "delete seeded data" filters WHERE
    // is_seeded = true; "delete all data" ignores the flag.
    public bool IsSeeded { get; set; }

    // Per-run marker set by the sample-data seeder to Guid.ToString("N") of the seed job
    // id — nullable string because real user rows carry no job id at all. The seed-page
    // Cancel action drops every row with a matching job_id in one transaction, so a
    // half-done or already-completed seed can be rolled back without touching real
    // events (job_id IS NULL) or other jobs' rows.
    public string? JobId { get; set; }
}
