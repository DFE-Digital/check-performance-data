namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// The media library's folders. The database keeps the tree sound whatever the caller does: a
/// parent must exist, two folders in one place may not share a name (ignoring case), a folder
/// cannot move inside itself, and a folder that holds images or folders cannot be deleted. Each
/// write is one tracked save, so the generic audit capture records it. A refused write clears the
/// scoped context's change tracker.
/// </summary>
public interface IMediaFolderRepository
{
    Task<IReadOnlyList<MediaFolderDto>> ListAsync(CancellationToken ct = default);

    Task<MediaFolderDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Adds a folder. Answers NameTaken or ParentNotFound when the database refuses it.</summary>
    Task<MediaFolderOutcome> CreateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default);

    /// <summary>Answers NotFound or NameTaken when the folder is not renamed.</summary>
    Task<MediaFolderOutcome> RenameAsync(Guid id, string name, CancellationToken ct = default);

    /// <summary>
    /// Puts the folder inside another folder, or at the top for null. Answers Cycle when that
    /// would put the folder inside itself, and NotFound, ParentNotFound or NameTaken.
    /// </summary>
    Task<MediaFolderOutcome> MoveAsync(Guid id, Guid? parentId, CancellationToken ct = default);

    /// <summary>Answers NotEmpty, and deletes nothing, while the folder holds an image or a folder.</summary>
    Task<MediaFolderOutcome> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>How many images and folders sit directly inside the folder.</summary>
    Task<(int Images, int Folders)> CountContentsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Puts the images in a folder, or at the top for null. Answers how many images changed folder;
    /// ids that are not in the library are skipped. Null when the folder no longer exists.
    /// </summary>
    Task<int?> MoveImagesAsync(IReadOnlyCollection<Guid> assetIds, Guid? folderId, CancellationToken ct = default);

    /// <summary>The folder and every folder inside it, at any depth, read with one recursive query.</summary>
    Task<IReadOnlyList<Guid>> SubtreeIdsAsync(Guid id, CancellationToken ct = default);
}
