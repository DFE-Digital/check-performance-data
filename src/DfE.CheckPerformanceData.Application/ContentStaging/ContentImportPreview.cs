namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A dry-run analysis of a bundle against the current environment, shown before the import is
// applied so an administrator can see exactly what will be added, what already exists (a
// collision), and decide what to do with each collision.
public sealed record ContentImportPreview(
    IReadOnlyList<PreviewItem> Pages,
    IReadOnlyList<PreviewItem> Blocks)
{
    // Blocked items (unknown parent) are neither new nor collisions — they cannot be imported.
    public int NewCount => Pages.Count(p => !p.Exists && !p.ParentMissing) + Blocks.Count(b => !b.Exists);
    public int CollisionCount => Pages.Count(p => p.Exists) + Blocks.Count(b => b.Exists);
    public int BlockedCount => Pages.Count(p => p.ParentMissing);
}

// One item in the bundle, matched against the target purely by stable GUID. Exists is true when a
// page/block with the same Id already exists (a collision); ExistingDescription explains it,
// including a rename (the target page/block currently has a different title/key). ParentMissing is
// true for a child whose parent GUID is not found in the bundle or the target — it cannot be
// imported (no orphan is created). Detail is the virtual slug path (pages) or block type.
public sealed record PreviewItem(
    Guid Id,
    string Title,
    string Detail,
    bool Exists,
    string? ExistingDescription,
    bool ParentMissing = false);
