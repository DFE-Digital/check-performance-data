namespace DfE.CheckPerformance.Persistence.Entities;

// One row per hit returned by a search request. ResultKind is either "page" or "block";
// ResultKey holds the canonical URL for a page or the block key for a block. Cascades
// with its parent SearchEvent, so a session-scoped purge in support drops the parent
// row and Postgres removes the children automatically without any explicit cleanup.
public sealed class SearchEventResult
{
    public long Id { get; set; }
    public long SearchEventId { get; set; }
    public int Position { get; set; }
    public string ResultKind { get; set; } = string.Empty;
    public string ResultKey { get; set; } = string.Empty;
    public float Rank { get; set; }

    // Mirrors the parent SearchEvent.IsSeeded so the delete-seeded query does not need
    // to JOIN back to the parent to filter children. Written by the sink at the same
    // time as the parent flag so the two stay in lockstep.
    public bool IsSeeded { get; set; }
}
