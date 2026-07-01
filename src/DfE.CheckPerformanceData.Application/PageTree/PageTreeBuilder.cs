namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Converts a flat sequence of <see cref="PageNodeTreeItemDto"/> rows into a nested tree
/// of <see cref="PageTreeNode"/> records, with children ordered by
/// <see cref="PageNodeTreeItemDto.SortOrder"/> at every level.
/// </summary>
/// <remarks>
/// <para><strong>Orphan-row handling:</strong> a row whose <c>ParentId</c> references an id
/// that is absent from the input set is promoted to a root. This is deliberate — if a parent
/// page is deleted, its former children should still appear in the admin tree so editors can
/// see and re-parent them, rather than disappearing silently.</para>
/// <para>The function is pure — no I/O, no side effects. Input order is irrelevant;
/// the caller supplies flat rows and gets back an immutable, sorted tree.</para>
/// </remarks>
public static class PageTreeBuilder
{
    /// <summary>
    /// Builds a nested tree from <paramref name="rows"/>.
    /// </summary>
    /// <param name="rows">Flat page rows from the repository query.</param>
    /// <returns>
    /// Root-level nodes sorted by <see cref="PageNodeTreeItemDto.SortOrder"/>, each carrying
    /// its own recursively sorted <see cref="PageTreeNode.Children"/>.
    /// </returns>
    public static IReadOnlyList<PageTreeNode> Build(IEnumerable<PageNodeTreeItemDto> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
            return Array.Empty<PageTreeNode>();

        // Collect all ids so we can detect orphans.
        var idSet = new HashSet<Guid>(list.Select(r => r.Id));

        // Index children by their real parent Guid (only when the parent exists in the input).
        // Avoids null dictionary keys, which .NET Dictionary does not support for value types.
        var childrenByParent = list
            .Where(r => r.ParentId.HasValue && idSet.Contains(r.ParentId.Value))
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.SortOrder).ToList());

        // Roots: ParentId is null, or ParentId refers to a missing id (orphan → promoted to root).
        var roots = list
            .Where(r => !r.ParentId.HasValue || !idSet.Contains(r.ParentId.Value))
            .OrderBy(r => r.SortOrder)
            .ToList();

        return roots
            .Select(row => MapNode(row, childrenByParent))
            .ToList();
    }

    private static PageTreeNode MapNode(
        PageNodeTreeItemDto row,
        Dictionary<Guid, List<PageNodeTreeItemDto>> childrenByParent)
    {
        IReadOnlyList<PageTreeNode> children = childrenByParent.TryGetValue(row.Id, out var childRows)
            ? childRows.Select(c => MapNode(c, childrenByParent)).ToList()
            : Array.Empty<PageTreeNode>();

        return new PageTreeNode(
            row.Id,
            row.Title,
            row.Path,
            row.PageType,
            row.HasLiveVersion,
            children);
    }
}
