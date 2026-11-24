namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>A media library folder. A null <see cref="ParentId"/> puts it at the top of the library.</summary>
public sealed record MediaFolderDto(Guid Id, string Name, Guid? ParentId);
