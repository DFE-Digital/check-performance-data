using DfE.CheckPerformanceData.Application.Media.Folders;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The page for one folder: rename it, move it into another folder, or delete it when it is empty.
/// Each form keeps what was typed, and its own error, when the change is refused.
/// </summary>
public sealed class MediaFolderViewModel
{
    public required MediaFolderDto Folder { get; init; }

    public required MediaFolderTree Folders { get; init; }

    public int ImageCount { get; init; }

    public int SubfolderCount { get; init; }

    public string Name { get; init; } = "";

    public string? NameError { get; init; }

    public string? MoveError { get; init; }

    public string? DeleteError { get; init; }

    public bool HasErrors => NameError is not null || MoveError is not null || DeleteError is not null;

    public bool CanDelete => ImageCount == 0 && SubfolderCount == 0;

    /// <summary>Why the folder cannot be deleted, or null when it can.</summary>
    public string? NotEmptyMessage => CanDelete ? null : MediaFolderService.NotEmptyError(Folder.Name, ImageCount, SubfolderCount);

    public IReadOnlyList<MediaFolderDto> Path => Folders.PathOf(Folder.Id);

    public string Url => FolderSettingsUrl(Folder.Id);

    public string LibraryUrl => LibraryUrlFor(Folder.Id);

    public string ParentLibraryUrl => LibraryUrlFor(Folder.ParentId);

    /// <summary>The folders this folder can move into: every folder outside it, in tree order, by path.</summary>
    public IReadOnlyList<MediaFolderOption> MoveTargets =>
        Folders.Flatten()
            .Where(i => !Folders.IsSameOrInside(i.Folder.Id, Folder.Id))
            .Select(i => new MediaFolderOption(i.Folder.Id, Folders.PathText(i.Folder.Id)))
            .ToList();

    public static string FolderSettingsUrl(Guid id) => $"/admin/media/folders/{id:D}";

    public static string LibraryUrlFor(Guid? id) => id is Guid value ? $"/admin/media?folder={value:D}" : "/admin/media";
}
