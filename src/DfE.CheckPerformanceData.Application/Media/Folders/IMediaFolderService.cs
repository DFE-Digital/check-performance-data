namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// Creates, renames, moves and deletes media library folders, and moves images between them.
/// Checks the name and refuses a move that would put a folder inside itself before it writes;
/// the database checks both again.
/// </summary>
public interface IMediaFolderService
{
    Task<MediaFolderTree> GetTreeAsync(CancellationToken ct = default);

    Task<MediaFolderResult> CreateAsync(string? name, Guid? parentId, CancellationToken ct = default);

    Task<MediaFolderResult> RenameAsync(Guid id, string? name, CancellationToken ct = default);

    Task<MediaFolderResult> MoveAsync(Guid id, Guid? parentId, CancellationToken ct = default);

    /// <summary>Deletes an empty folder. A folder that holds images or folders is refused with a message that says what it holds.</summary>
    Task<MediaFolderResult> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<MediaImageMoveResult> MoveImagesAsync(IReadOnlyCollection<Guid> assetIds, Guid? folderId, CancellationToken ct = default);
}
