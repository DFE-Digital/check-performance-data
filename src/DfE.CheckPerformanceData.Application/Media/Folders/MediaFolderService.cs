namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// Folder changes for the media library. The service checks the name, and refuses a move into the
/// folder itself or into a folder inside it, before it writes. The database checks the same rules
/// again (a unique name in each place, a trigger that refuses a loop, and keys that refuse to
/// delete a folder that holds anything). So two editors who change the tree at the same time get a
/// refusal, never a broken tree.
/// </summary>
public sealed class MediaFolderService(IMediaFolderRepository folders) : IMediaFolderService
{
    public const string CycleError = "You cannot move a folder into itself or into a folder inside it";
    public const string ParentGoneError = "The folder you chose no longer exists. Choose another folder";
    public const string FolderGoneError = "The folder no longer exists";
    public const string NoImagesError = "Select one or more images to move";

    public async Task<MediaFolderTree> GetTreeAsync(CancellationToken ct = default) =>
        new(await folders.ListAsync(ct));

    public async Task<MediaFolderResult> CreateAsync(string? name, Guid? parentId, CancellationToken ct = default)
    {
        var (clean, error) = MediaFolderNames.Check(name);
        if (clean is null) return MediaFolderResult.Refused(MediaFolderOutcome.NameInvalid, error!);
        if (parentId is Guid parent && await folders.GetAsync(parent, ct) is null)
            return MediaFolderResult.Refused(MediaFolderOutcome.ParentNotFound, ParentGoneError);

        var folder = new MediaFolderDto(Guid.NewGuid(), clean, parentId);
        var outcome = await folders.CreateAsync(folder.Id, clean, parentId, ct);
        return outcome == MediaFolderOutcome.Done ? MediaFolderResult.Done(folder) : Refusal(outcome, clean);
    }

    public async Task<MediaFolderResult> RenameAsync(Guid id, string? name, CancellationToken ct = default)
    {
        var existing = await folders.GetAsync(id, ct);
        if (existing is null) return MediaFolderResult.Refused(MediaFolderOutcome.NotFound, FolderGoneError);

        var (clean, error) = MediaFolderNames.Check(name);
        if (clean is null) return MediaFolderResult.Refused(MediaFolderOutcome.NameInvalid, error!);

        var outcome = await folders.RenameAsync(id, clean, ct);
        return outcome == MediaFolderOutcome.Done ? MediaFolderResult.Done(existing with { Name = clean }) : Refusal(outcome, clean);
    }

    public async Task<MediaFolderResult> MoveAsync(Guid id, Guid? parentId, CancellationToken ct = default)
    {
        var tree = await GetTreeAsync(ct);
        var existing = tree.Find(id);
        if (existing is null) return MediaFolderResult.Refused(MediaFolderOutcome.NotFound, FolderGoneError);
        if (parentId is Guid parent)
        {
            if (tree.Find(parent) is null) return MediaFolderResult.Refused(MediaFolderOutcome.ParentNotFound, ParentGoneError);
            if (tree.IsSameOrInside(parent, id)) return MediaFolderResult.Refused(MediaFolderOutcome.Cycle, CycleError);
        }

        var outcome = await folders.MoveAsync(id, parentId, ct);
        return outcome == MediaFolderOutcome.Done ? MediaFolderResult.Done(existing with { ParentId = parentId }) : Refusal(outcome, existing.Name);
    }

    public async Task<MediaFolderResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await folders.GetAsync(id, ct);
        if (existing is null) return MediaFolderResult.Refused(MediaFolderOutcome.NotFound, FolderGoneError);

        var (images, subfolders) = await folders.CountContentsAsync(id, ct);
        if (images > 0 || subfolders > 0)
            return MediaFolderResult.Refused(MediaFolderOutcome.NotEmpty, NotEmptyError(existing.Name, images, subfolders));

        var outcome = await folders.DeleteAsync(id, ct);
        if (outcome == MediaFolderOutcome.Done) return MediaFolderResult.Done(existing);
        if (outcome == MediaFolderOutcome.NotEmpty)
        {
            // Something was put in the folder after the count.
            var (nowImages, nowFolders) = await folders.CountContentsAsync(id, ct);
            return MediaFolderResult.Refused(outcome, NotEmptyError(existing.Name, nowImages, nowFolders));
        }
        return Refusal(outcome, existing.Name);
    }

    public Task<(int Images, int Folders)> CountContentsAsync(Guid id, CancellationToken ct = default) =>
        folders.CountContentsAsync(id, ct);

    public async Task<MediaImageMoveResult> MoveImagesAsync(IReadOnlyCollection<Guid> assetIds, Guid? folderId, CancellationToken ct = default)
    {
        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new MediaImageMoveResult(MediaFolderOutcome.NotFound, 0, NoImagesError);

        var moved = await folders.MoveImagesAsync(ids, folderId, ct);
        return moved is int count
            ? new MediaImageMoveResult(MediaFolderOutcome.Done, count)
            : new MediaImageMoveResult(MediaFolderOutcome.NotFound, 0, FolderGoneError);
    }

    /// <summary>The message for a folder that cannot be deleted because it holds images or folders.</summary>
    public static string NotEmptyError(string name, int images, int subfolders)
    {
        var parts = new List<string>();
        if (images > 0) parts.Add(images == 1 ? "1 image" : $"{images} images");
        if (subfolders > 0) parts.Add(subfolders == 1 ? "1 folder" : $"{subfolders} folders");
        var holds = parts.Count == 0 ? "images or folders" : string.Join(" and ", parts);
        return $"You cannot delete ‘{name}’ because it holds {holds}. Move or delete them first.";
    }

    private static MediaFolderResult Refusal(MediaFolderOutcome outcome, string name) => outcome switch
    {
        MediaFolderOutcome.NameTaken => MediaFolderResult.Refused(outcome, MediaFolderNames.Taken(name)),
        MediaFolderOutcome.Cycle => MediaFolderResult.Refused(outcome, CycleError),
        MediaFolderOutcome.ParentNotFound => MediaFolderResult.Refused(outcome, ParentGoneError),
        MediaFolderOutcome.NotEmpty => MediaFolderResult.Refused(outcome, NotEmptyError(name, 0, 0)),
        _ => MediaFolderResult.Refused(MediaFolderOutcome.NotFound, FolderGoneError),
    };
}
