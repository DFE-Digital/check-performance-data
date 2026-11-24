namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// Every folder of the library in memory, for the nav tree, the breadcrumbs, the folder lists and
/// the move checks. The library holds a few hundred folders at most, so one query reads them all.
/// Folders sort by name, ignoring case. Every walk up the tree stops at a folder it has seen, so a
/// cycle in bad data cannot hang a page.
/// </summary>
public sealed class MediaFolderTree
{
    private readonly Dictionary<Guid, MediaFolderDto> _byId;
    private readonly Dictionary<Guid, List<MediaFolderDto>> _children = [];
    private readonly List<MediaFolderDto> _roots = [];

    public static MediaFolderTree Empty { get; } = new([]);

    public MediaFolderTree(IEnumerable<MediaFolderDto> folders)
    {
        _byId = folders.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var folder in _byId.Values)
        {
            // A folder whose parent is missing is shown at the top rather than lost.
            if (folder.ParentId is Guid parent && _byId.ContainsKey(parent) && parent != folder.Id)
            {
                if (!_children.TryGetValue(parent, out var list)) _children[parent] = list = [];
                list.Add(folder);
            }
            else
            {
                _roots.Add(folder);
            }
        }

        Sort(_roots);
        foreach (var list in _children.Values) Sort(list);
    }

    public int Count => _byId.Count;

    public MediaFolderDto? Find(Guid? id) => id is Guid value && _byId.TryGetValue(value, out var folder) ? folder : null;

    /// <summary>The folders directly inside a folder, or at the top of the library for null.</summary>
    public IReadOnlyList<MediaFolderDto> ChildrenOf(Guid? parentId) =>
        parentId is null ? _roots : _children.TryGetValue(parentId.Value, out var list) ? list : [];

    /// <summary>The folder and the folders above it, from the top of the library down. Empty for an unknown id.</summary>
    public IReadOnlyList<MediaFolderDto> PathOf(Guid? id)
    {
        var path = new List<MediaFolderDto>();
        var seen = new HashSet<Guid>();
        var current = Find(id);
        while (current is not null && seen.Add(current.Id))
        {
            path.Add(current);
            current = Find(current.ParentId);
        }
        path.Reverse();
        return path;
    }

    /// <summary>The folder's names from the top down, joined by " / ". Empty for the top of the library.</summary>
    public string PathText(Guid? id) => string.Join(" / ", PathOf(id).Select(f => f.Name));

    /// <summary>True when <paramref name="candidate"/> is <paramref name="ancestor"/> or sits anywhere inside it.</summary>
    public bool IsSameOrInside(Guid candidate, Guid ancestor) => PathOf(candidate).Any(f => f.Id == ancestor);

    /// <summary>The folder and every folder inside it, at any depth.</summary>
    public IReadOnlySet<Guid> SubtreeOf(Guid id)
    {
        var ids = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        if (_byId.ContainsKey(id)) stack.Push(id);
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!ids.Add(next)) continue;
            foreach (var child in ChildrenOf(next)) stack.Push(child.Id);
        }
        return ids;
    }

    /// <summary>Every folder in tree order (each folder followed by its subfolders), with its depth.</summary>
    public IReadOnlyList<MediaFolderTreeItem> Flatten()
    {
        var items = new List<MediaFolderTreeItem>();
        var seen = new HashSet<Guid>();
        void Walk(IReadOnlyList<MediaFolderDto> level, int depth)
        {
            foreach (var folder in level)
            {
                if (!seen.Add(folder.Id)) continue;
                items.Add(new MediaFolderTreeItem(folder, depth));
                Walk(ChildrenOf(folder.Id), depth + 1);
            }
        }
        Walk(_roots, 0);
        return items;
    }

    private static void Sort(List<MediaFolderDto> folders) =>
        folders.Sort((a, b) =>
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            if (byName != 0) return byName;
            byName = StringComparer.Ordinal.Compare(a.Name, b.Name);
            return byName != 0 ? byName : a.Id.CompareTo(b.Id);
        });
}
