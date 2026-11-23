namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>A folder in tree order, with its depth: 0 for a folder at the top of the library.</summary>
public sealed record MediaFolderTreeItem(MediaFolderDto Folder, int Depth);
