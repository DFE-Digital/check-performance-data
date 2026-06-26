namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A dry-run analysis of a bundle against the current environment, shown before the import is
// applied so an administrator can see exactly what will be added, what already exists (a
// collision), and decide what to do with each collision.
public sealed record ContentImportPreview(
    IReadOnlyList<PreviewItem> Pages,
    IReadOnlyList<PreviewItem> Blocks)
{
    public int NewCount => Pages.Count(p => !p.Exists) + Blocks.Count(b => !b.Exists);
    public int CollisionCount => Pages.Count(p => p.Exists) + Blocks.Count(b => b.Exists);
}

// One item in the bundle. Exists is true when it collides with existing content (matched by
// stable GUID, or by a slug/Key clash). ExistingDescription explains the collision — including a
// rename, e.g. the bundle's "B" landing on the target's renamed "C".
public sealed record PreviewItem(
    Guid Id,
    string Title,
    string Detail,
    bool Exists,
    string? ExistingDescription);
