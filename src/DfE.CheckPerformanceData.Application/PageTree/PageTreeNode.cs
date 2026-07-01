namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// A node in the rendered admin page tree.
/// Carries the display fields needed for navigation and the recursively nested
/// <see cref="Children"/> list, already sorted by <see cref="PageNodeTreeItemDto.SortOrder"/>.
/// </summary>
public sealed record PageTreeNode(
    Guid Id,
    string Title,
    string Path,
    string PageType,
    bool HasLiveVersion,
    IReadOnlyList<PageTreeNode> Children);
