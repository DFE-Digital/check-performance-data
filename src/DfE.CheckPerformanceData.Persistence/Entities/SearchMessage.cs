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
}
