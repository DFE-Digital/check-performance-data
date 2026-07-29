namespace DfE.CheckPerformance.Persistence.Entities;

// A user-submitted feedback message about a search result. Free-standing table keyed by
// the same session identifier that the events table uses, but deliberately WITHOUT a
// foreign key to search_events — messages must survive the shorter events-retention
// window (support cases often reference weeks-old sessions where the underlying event
// rows have already been purged). Email is nullable: when the user ticks "hide my
// email" on the form, the controller drops the value before persist rather than storing
// an encrypted or masked variant — the simplest privacy model is to not store what
// isn't needed.
public sealed class SearchMessage
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public string WhatLookingFor { get; set; } = string.Empty;
    public string? WhatGot { get; set; }
    public string? Email { get; set; }
    public bool IsRead { get; set; }
    public string? ReadByAdminSub { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    // True when the row was written by the sample-data seeder (dev-only Test-data admin
    // surface); false for every message submitted by a real user. Admin "delete seeded
    // data" filters WHERE is_seeded = true so a real user's feedback survives the wipe.
    public bool IsSeeded { get; set; }

    // Per-run marker set by the sample-data seeder to Guid.ToString("N") of the seed job
    // id — nullable string because real user-submitted messages carry no job id. The
    // seed-page Cancel action rolls this job's messages back via WHERE job_id = @id in
    // the same transaction that drops the matching events + children.
    public string? JobId { get; set; }
}
