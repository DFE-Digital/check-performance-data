namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A dry-run analysis of a bundle against the current environment, shown before the import is
// applied so an administrator can see exactly what will be added, what already exists (a
// collision), and decide what to do with each collision.
public sealed record ContentImportPreview(
    IReadOnlyList<PreviewItem> Pages,
    IReadOnlyList<PreviewItem> Blocks,
    IReadOnlyList<PreviewItem> Banners)
{
    // Blocked items (a problem of their own, or an unknown parent) are neither new nor
    // collisions — they cannot be imported.
    public int NewCount => All.Count(i => !i.Exists && !i.Blocked);
    public int CollisionCount => All.Count(i => i.Exists && !i.Blocked);
    public int BlockedCount => All.Count(i => i.Blocked);

    // What the import will put right and say so, such as a segment it will lowercase.
    public IReadOnlyList<string> Warnings { get; init; } = [];

    private IEnumerable<PreviewItem> All => Pages.Concat(Blocks).Concat(Banners);
}

// One item in the bundle, matched against the target purely by stable GUID. Exists is true when a
// page/block with the same Id already exists (a collision); ExistingDescription explains it,
// including a rename (the target page/block currently has a different title/key). ParentMissing is
// true for a child whose parent GUID is not found in the bundle or the target — it cannot be
// imported (no orphan is created). Problem says why an item cannot be imported on its own account,
// such as a segment that breaks the rules. Detail is the virtual slug path (pages) or block type.
public sealed record PreviewItem(
    Guid Id,
    string Title,
    string Detail,
    bool Exists,
    string? ExistingDescription,
    bool ParentMissing = false,
    string? Problem = null)
{
    public bool Blocked => ParentMissing || Problem is not null;
}
