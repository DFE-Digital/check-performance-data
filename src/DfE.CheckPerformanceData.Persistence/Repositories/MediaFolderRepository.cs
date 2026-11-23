using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Persistence.Configurations;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// The media library's folders. Every write is a tracked save, so the generic audit capture
// records each folder change (MediaFolder rows) and each image move (a MediaAsset Update of
// FolderId). The database is the final judge: a refused save is turned into an outcome, and the
// change tracker is cleared so a later save on the scoped context does not try it again.
public sealed class MediaFolderRepository(IPortalDbContext context, ICurrentUserService currentUser) : IMediaFolderRepository
{
    public async Task<IReadOnlyList<MediaFolderDto>> ListAsync(CancellationToken ct = default) =>
        await context.MediaFolders.AsNoTracking()
            .OrderBy(f => f.NameKey).ThenBy(f => f.Id)
            .Select(f => new MediaFolderDto(f.Id, f.Name, f.ParentId))
            .ToListAsync(ct);

    public async Task<MediaFolderDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        await context.MediaFolders.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new MediaFolderDto(f.Id, f.Name, f.ParentId))
            .FirstOrDefaultAsync(ct);

    public async Task<MediaFolderOutcome> CreateAsync(Guid id, string name, Guid? parentId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        context.MediaFolders.Add(new MediaFolder
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            CreatedAtUtc = now,
            CreatedBy = currentUser.Email,
            UpdatedAtUtc = now,
            UpdatedBy = currentUser.Email,
        });
        return await SaveAsync(ct);
    }

    public async Task<MediaFolderOutcome> RenameAsync(Guid id, string name, CancellationToken ct = default)
    {
        var folder = await context.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return MediaFolderOutcome.NotFound;
        if (folder.Name == name) return MediaFolderOutcome.Done;

        folder.Name = name;
        Touch(folder);
        return await SaveAsync(ct);
    }

    public async Task<MediaFolderOutcome> MoveAsync(Guid id, Guid? parentId, CancellationToken ct = default)
    {
        var folder = await context.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return MediaFolderOutcome.NotFound;
        if (folder.ParentId == parentId) return MediaFolderOutcome.Done;

        folder.ParentId = parentId;
        Touch(folder);
        return await SaveAsync(ct);
    }

    public async Task<MediaFolderOutcome> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await context.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null) return MediaFolderOutcome.NotFound;

        context.MediaFolders.Remove(folder);
        return await SaveAsync(ct);
    }

    public async Task<(int Images, int Folders)> CountContentsAsync(Guid id, CancellationToken ct = default)
    {
        var images = await context.MediaAssets.CountAsync(a => a.FolderId == id, ct);
        var folders = await context.MediaFolders.CountAsync(f => f.ParentId == id, ct);
        return (images, folders);
    }

    public async Task<int?> MoveImagesAsync(IReadOnlyCollection<Guid> assetIds, Guid? folderId, CancellationToken ct = default)
    {
        if (folderId is Guid target && !await context.MediaFolders.AnyAsync(f => f.Id == target, ct)) return null;
        if (assetIds.Count == 0) return 0;

        var ids = assetIds.Distinct().ToList();
        var assets = await context.MediaAssets
            .Where(a => ids.Contains(a.Id) && a.FolderId != folderId)
            .ToListAsync(ct);
        if (assets.Count == 0) return 0;

        foreach (var asset in assets) asset.FolderId = folderId;
        return await SaveAsync(ct) switch
        {
            MediaFolderOutcome.Done => assets.Count,
            _ => null, // the folder was deleted after the check
        };
    }

    public async Task<IReadOnlyList<Guid>> SubtreeIdsAsync(Guid id, CancellationToken ct = default) =>
        await MediaFolderQueries.SubtreeIds(context, id).ToListAsync(ct);

    private void Touch(MediaFolder folder)
    {
        folder.UpdatedAtUtc = DateTime.UtcNow;
        folder.UpdatedBy = currentUser.Email;
    }

    private async Task<MediaFolderOutcome> SaveAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
            return MediaFolderOutcome.Done;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            var outcome = pg switch
            {
                { SqlState: PostgresErrorCodes.UniqueViolation } => MediaFolderOutcome.NameTaken,
                { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: MediaFolderConfiguration.NoCycleConstraint } => MediaFolderOutcome.Cycle,
                // A delete that a folder or an image still points at, or a parent that is gone.
                { SqlState: PostgresErrorCodes.ForeignKeyViolation } when HasDelete() => MediaFolderOutcome.NotEmpty,
                { SqlState: PostgresErrorCodes.ForeignKeyViolation } => MediaFolderOutcome.ParentNotFound,
                _ => (MediaFolderOutcome?)null,
            };
            if (outcome is null) throw;
            context.ChangeTracker.Clear();
            return outcome.Value;
        }
    }

    private bool HasDelete() => context.ChangeTracker.Entries<MediaFolder>().Any(e => e.State == EntityState.Deleted);
}
