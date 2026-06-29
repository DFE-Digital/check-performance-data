namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Summary of what an import did, so the admin UI can report "added N, updated M, skipped K"
// and surface any items that could not be applied (e.g. a child whose parent was missing).
public sealed class ContentImportResult
{
    public int WikiPagesCreated { get; set; }
    public int WikiPagesUpdated { get; set; }
    public int WikiPagesSkipped { get; set; }

    public int ContentBlocksCreated { get; set; }
    public int ContentBlocksUpdated { get; set; }
    public int ContentBlocksSkipped { get; set; }

    // Human-readable notes about items that were skipped.
    public List<string> Warnings { get; } = [];

    // Items that could not be applied at all — e.g. a child whose parent GUID is unknown, so no
    // orphan is created. Surfaced prominently rather than as a passing note.
    public List<string> Errors { get; } = [];
}
