namespace DfE.CheckPerformanceData.Application.ContentPages;

// Pure tree-mutation operations backing the inline editor's add / move / delete / nest actions. A
// position is addressed by a path of <see cref="TreeStep"/>s (see TreeStep for the column/index
// convention). All operations resolve the path to (containing column, index) and act in place;
// move operations clamp at the ends rather than throw.
public static class ContentTreeEditor
{
    // Inserts node at the position the path addresses, shifting existing siblings down. An index
    // equal to the column's length appends.
    public static void InsertAt(List<ContentNode> root, IReadOnlyList<TreeStep> path, ContentNode node)
    {
        var (column, index) = Resolve(root, path);
        column.Insert(index, node);
    }

    // Removes and returns the node the path addresses.
    public static ContentNode Remove(List<ContentNode> root, IReadOnlyList<TreeStep> path)
    {
        var (column, index) = Resolve(root, path);
        var node = column[index];
        column.RemoveAt(index);
        return node;
    }

    // Swaps the addressed node with its previous sibling; no-op at the first position.
    public static void MoveUp(List<ContentNode> root, IReadOnlyList<TreeStep> path)
    {
        var (column, index) = Resolve(root, path);
        if (index > 0)
            (column[index - 1], column[index]) = (column[index], column[index - 1]);
    }

    // Swaps the addressed node with its next sibling; no-op at the last position.
    public static void MoveDown(List<ContentNode> root, IReadOnlyList<TreeStep> path)
    {
        var (column, index) = Resolve(root, path);
        if (index < column.Count - 1)
            (column[index], column[index + 1]) = (column[index + 1], column[index]);
    }

    // The node the path addresses (without removing it).
    public static ContentNode NodeAt(List<ContentNode> root, IReadOnlyList<TreeStep> path)
    {
        var (column, index) = Resolve(root, path);
        return column[index];
    }

    // Walks the path, returning the column that contains the addressed position and the index
    // within it. Each step after the first descends into the previously-addressed region's column.
    private static (List<ContentNode> Column, int Index) Resolve(List<ContentNode> root, IReadOnlyList<TreeStep> path)
    {
        if (path.Count == 0)
            throw new ArgumentException("Path must have at least one step.", nameof(path));

        var column = root;
        for (var depth = 0; depth < path.Count - 1; depth++)
        {
            var node = column[path[depth].Index];
            if (node is not RegionNode region)
                throw new InvalidOperationException("Path descends through a non-region node.");
            column = region.Columns[path[depth + 1].Column];
        }

        return (column, path[^1].Index);
    }
}
