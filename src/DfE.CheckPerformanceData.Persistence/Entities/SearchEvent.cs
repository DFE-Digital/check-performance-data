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
    public int ResultsTotal { get; set; }
    public bool ZeroResults { get; set; }
    public int LatencyMs { get; set; }

    // True when the row was written by the sample-data seeder (dev-only Test-data admin
    // surface); false for every event captured from a real user request. Existing rows
    // default to false so the marker's write-once invariant survives the initial
    // migration without a data backfill. Admin "delete seeded data" filters WHERE
    // is_seeded = true; "delete all data" ignores the flag.
    public bool IsSeeded { get; set; }
}
