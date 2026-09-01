namespace DfE.CheckPerformance.Persistence.Entities;

// A parsed import bundle parked between the Preview step and the Import step. One row per
// preview an administrator has looked at but not yet confirmed; the row goes when the import
// succeeds, or when its expiry passes and the next preview sweeps it.
//
// The bundle is held as its canonical JSON rather than a blob of POCOs: it is what the import
// path already parses, it survives a schema change to the in-memory model, and Postgres will
// compress a large text column out of line without being asked.
public sealed class ContentStagingSession
{
    public Guid Id { get; set; }

    // The canonical JSON of the parsed bundle — what Import re-parses and applies.
    public string BundleJson { get; set; } = string.Empty;

    // Who previewed it. Recorded for the audit trail and so a support query about an abandoned
    // import has a name attached; never used to authorise the import itself.
    public string? CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
